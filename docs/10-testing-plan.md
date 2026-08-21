# Testing plan

## 1. Strategy

Three layers, each answering a different question, with no attempt to hit a coverage number:

| Layer | Question it answers | Tooling | Speed |
|---|---|---|---|
| Domain + Application unit tests | Do the rules hold in isolation? | xUnit, NSubstitute, FluentAssertions, AutoFixture-free explicit builders | milliseconds, no I/O |
| API integration tests | Does the whole stack behave over HTTP, against a real database? | `WebApplicationFactory`, Testcontainers SQL Server 2022, Respawn | seconds per class |
| Frontend tests | Do services, guards, interceptors and components behave? | Vitest + Angular testing utilities, `HttpTestingController` | milliseconds |
| Browser tests | Do the SPA and the API actually meet, is the interface accessible, does it hold together at four widths? | Playwright, Chromium, axe-core; eight specs (section 4b) | minutes |

The database in integration tests is **real SQL Server in Docker**, not the in-memory provider. Global query
filters, `LIKE` translation, `OFFSET/FETCH`, unique-index violations and `datetimeoffset` behaviour are
exactly the things the in-memory provider gets wrong, and they are exactly what needs proving here. Fallback
for a machine without Docker: a configurable connection string in `USERMANAGEMENT_TEST_SQL`, documented in the
README. That fallback gives **each fixture its own database** - the API fixture and the persistence fixture run
in parallel collections, and pointing both at one database made 122 of 153 tests fail with what looked like a
broken SQL Server. On the Testcontainers path the separation is free, because each fixture owns a container.

## 2. Unit tests

### Domain (`UnitTests/Domain`)

| Test | Asserts |
|---|---|
| `SoftDelete_sets_flag_timestamp_and_actor` | BR-07 fields populated together |
| `SoftDelete_on_deleted_user_throws` | BR-07 |
| `Restore_on_active_user_throws` | BR-08 |
| `ChangeRole_rotates_security_stamp` | BR-14 precondition |
| `ChangeRole_to_same_role_is_a_no_op` | no spurious audit row |
| `SetPasswordHash_rotates_security_stamp` | BR-13 precondition |
| `RecordFailedLogin_locks_after_threshold` | BR-12 boundary at 4, 5, 6 attempts |
| `RecordSuccessfulLogin_clears_counters` | lockout state reset |
| `IsLockedOut_respects_provider_time` | no `DateTime.UtcNow` inside the entity |

### Application handlers (`UnitTests/Application`)

| Test | Asserts |
|---|---|
| `Login_with_valid_credentials_returns_token_and_stamps_last_login` | C-01, C-04 |
| `Login_with_wrong_password_records_failure_and_throws_generic_error` | T-01, T-12 |
| `Login_with_unknown_username_verifies_a_dummy_hash` | timing-difference mitigation actually runs |
| `Login_for_deleted_user_is_rejected_with_generic_error` | BR-05 |
| `Login_while_locked_out_is_rejected_without_disclosing_lockout` | BR-12 |
| `CreateUser_hashes_password_and_never_stores_plaintext` | C-02 |
| `CreateUser_with_duplicate_username_throws_conflict` | BR-09 |
| `CreateUser_with_duplicate_email_throws_conflict` | BR-09 |
| `UpdateUser_changing_own_role_throws_unprocessable` | BR-04 |
| `UpdateUser_demoting_last_admin_throws_conflict` | BR-03 |
| `UpdateUser_revokes_refresh_tokens_on_role_change` | BR-14 |
| `DeleteUser_self_throws_forbidden` | BR-01 |
| `DeleteUser_last_admin_throws_conflict` | BR-02 |
| `DeleteUser_already_deleted_throws_conflict` | BR-07 |
| `RestoreUser_active_throws_conflict` | BR-08 |
| `UpdateMyProfile_updates_only_name_and_email_of_the_token_subject` | D-05, D-06 |
| `ChangeMyPassword_with_wrong_current_password_throws` | BR-11 path |
| `ChangeMyPassword_revokes_all_refresh_tokens` | BR-13 |
| `GetUsers_rejects_unknown_sort_field` | E-08 |
| `GetUsers_rejects_page_size_above_cap` | E-07 |
| `GetUsers_defaults_to_createdAt_desc_with_id_tiebreaker` | paging stability |
| `GetDeletedUsers_is_the_only_handler_that_ignores_query_filters` | BR-15, ADR-0004 |
| `RestoreUser_when_username_is_taken_by_an_active_user_throws_conflict` | BR-17 |
| `UpdateUser_with_a_stale_row_version_surfaces_as_a_concurrency_conflict` | BR-16 |
| `CreateUser_ignores_soft_deleted_rows_when_checking_uniqueness` | BR-09, ADR-0009 |

### Refresh-token family behaviour

| Test | Asserts |
|---|---|
| `Refresh_rotates_the_token_and_keeps_the_family` | successor inherits `FamilyId`, predecessor gets `RevocationReason = Rotated` and `ReplacedByTokenId` |
| `Refresh_with_an_already_rotated_token_revokes_the_entire_family` | reuse detection, `RevocationReason = ReuseDetected` (T-05) |
| `Refresh_reuse_does_not_revoke_other_families_of_the_same_user` | per-lineage revocation, so one compromised device does not sign the user out everywhere |
| `Refresh_with_an_expired_or_revoked_token_fails` | no resurrection |
| `Password_change_revokes_every_family` | BR-13 |
| `Role_change_revokes_every_family` | BR-14 |
| `Raw_refresh_token_is_never_persisted` | the stored value equals `SHA256(raw)` and never the raw string |

### Validators, auditing, architecture

| Test | Asserts |
|---|---|
| One class per validator | required, length, format, and policy rules including all password-policy boundaries |
| `AuditEntryBuilder_redacts_password_hash_and_security_stamp` | F-03 |
| `AuditEntryBuilder_emits_RoleChange_in_addition_to_Update` | F-01 |
| `AuditEntryBuilder_captures_old_and_new_values_for_changed_properties_only` | payload stays small and readable |
| `Application_must_not_depend_on_AspNetCore` | A-03 |
| `Domain_must_have_no_project_dependencies` | A-02 |
| `Infrastructure_must_not_be_referenced_outside_the_composition_root` | A-01 |

## 3. Integration tests

Shared fixture: one SQL Server container per test run, database created by migrations, Respawn resetting
data between test classes, and an `AuthenticatedClientFactory` producing `HttpClient`s pre-authenticated as
`admin`, `jdoe` or `readonly`.

### Authorization matrix test (the assignment's central proof)

One `[Theory]` over `(role, method, route, expectedStatus)` covering every mutating endpoint for all three
roles, so the matrix in [07-authorization-matrix.md](07-authorization-matrix.md) is executable rather than
prose:

```text
Admin        POST   /api/users            -> 201
User         POST   /api/users            -> 403
ReadOnlyUser POST   /api/users            -> 403
Admin        PUT    /api/users/{other}    -> 200
User         PUT    /api/users/{other}    -> 403
ReadOnlyUser PUT    /api/users/{other}    -> 403
Admin        DELETE /api/users/{other}    -> 204
User         DELETE /api/users/{other}    -> 403
ReadOnlyUser DELETE /api/users/{other}    -> 403
Admin        POST   /api/users/{id}/restore -> 204
User         POST   /api/users/{id}/restore -> 403
Admin        GET    /api/audit-logs       -> 200
User         GET    /api/audit-logs       -> 403
ReadOnlyUser GET    /api/audit-logs       -> 403
all three    GET    /api/users            -> 200
all three    PUT    /api/users/me         -> 200
```

### Flow tests

| Test | Asserts |
|---|---|
| `Login_returns_token_and_sets_httponly_refresh_cookie` | cookie flags `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/auth` |
| `Refresh_rotates_the_cookie_and_returns_a_new_access_token` | C-11 |
| `Logout_revokes_the_refresh_token` | subsequent refresh -> 401 |
| `Expired_token_returns_401` | issued with a backdated clock |
| `Malformed_token_returns_401` | tampered signature |
| `Create_then_get_then_update_then_delete_user` | full CRUD with correct codes and `Location` |
| `Deleted_user_is_absent_from_the_list_and_from_get_by_id_for_non_admin` | E-10, T-10 |
| `Admin_can_list_deleted_users_via_the_deleted_route` | E-11 |
| `Deleted_route_returns_403_for_user_and_readonly` | BR-15, T-10 — the one route where a mistake exposes removed people's data |
| `Unknown_query_parameters_do_not_reveal_deleted_rows` | posts `?includeDeleted=true` and asserts the parameter is inert, not honoured |
| `Soft_deleted_username_and_email_can_be_reused_by_a_new_active_user` | ADR-0009, filtered unique index |
| `Restore_after_the_username_was_reused_returns_409` | BR-17 |
| `Concurrent_update_of_one_user_returns_409_RESOURCE_MODIFIED` | BR-16 — two clients read, both update, the second is refused and the row still holds the first client's values |
| `Update_without_a_row_version_returns_400` | ADR-0013 — the token is required, not optional |
| `Row_version_changes_after_every_successful_update` | proves the token is live, not a constant |
| `Deleted_user_cannot_log_in` | BR-05 end to end |
| `Search_matches_username_email_first_and_last_name` | E-04 |
| `Search_is_translated_to_SQL_not_evaluated_in_memory` | asserts on the captured command text via an EF interceptor |
| `Sorting_by_each_whitelisted_field_orders_correctly_in_both_directions` | E-08 |
| `Unknown_sort_field_returns_400_with_INVALID_SORT_FIELD` | E-08 |
| `Page_size_101_returns_400`, `Page_beyond_last_returns_empty_page_with_metadata` | E-06, E-07 |
| `Role_filter_returns_only_that_role` | E-09 |
| `Profile_update_by_a_user_cannot_touch_another_user` | D-05 |
| `Profile_payload_containing_roleId_returns_400` | D-06 |
| `Admin_changing_own_role_returns_422` | BR-04 |
| `Deleting_the_last_admin_returns_409` | BR-02 |
| `Audit_row_written_for_insert_update_delete_restore_and_role_change` | F-01, F-02 |
| `Audit_rows_never_contain_password_material` | F-03 |
| `Password_change_flow_writes_no_password_material_to_any_audit_row` | audit policy 4.5, end to end |
| `Audit_rows_never_contain_token_material` | audit policy 4.5 |
| `Audit_rows_are_immutable_across_subsequent_operations` | F-04 |
| `Audit_row_records_the_target_id_display_name_ip_and_correlation_id` | F-05, F-12, ADR-0009 |
| `Audit_row_for_a_reused_username_still_names_the_original_user_id` | ADR-0009 — the reversal's core claim, proven |
| `Audit_excludes_row_version_stamps_and_login_counters` | audit policy 4.3 |
| `Error_responses_are_ProblemDetails_with_errorCode_and_traceId` | G-03 |
| `Unhandled_exception_returns_500_without_detail` | G-04 (a test-only endpoint that throws, registered in the test host) |
| `Validation_errors_are_Arabic_under_Accept_Language_ar_with_a_stable_errorCode` | H-01 |
| `Security_headers_present_on_a_real_response` | section 5 of the security plan |

## 4. Frontend tests

| Area | Test |
|---|---|
| `AuthService` | login stores the session in memory only; a test asserts `localStorage` and `sessionStorage` are untouched after login |
| `AuthService` | logout clears the session signal and calls the API |
| `authGuard` | unauthenticated -> redirect to `/login` with `returnUrl`; authenticated -> pass |
| `roleGuard` | wrong role -> `/forbidden`; right role -> pass |
| `authTokenInterceptor` | attaches the bearer header when a session exists, omits it otherwise, and never attaches it to the login request |
| `apiErrorInterceptor` | maps 400/401/403/404/409/422/500 into the typed `ApiError` union with the `errorCode` preserved |
| `authRefreshInterceptor` | a single 401 triggers one refresh and replays the request; **concurrent** 401s trigger exactly one refresh call; a failed refresh clears the session and routes to login |
| `correlationIdInterceptor` | sets `X-Correlation-Id` on every request |
| `UsersListPage` | renders rows; shows the loading state, then the empty state for zero results, then the error state on failure |
| `UsersListPage` | search input debounces and writes `search` to the URL; page, sort and role filter round-trip through query params |
| `UsersListPage` | sorting a column issues a request with the mapped `sortBy`/`sortDirection` |
| `UsersListPage` | Admin sees create/edit/delete controls; `User` and `ReadOnlyUser` do not |
| `UserFormPage` | required/format/length validation messages; submit disabled while pending; server `409` maps to a field-level error on username or email |
| `UserFormPage` | async uniqueness validator fires **on blur only**, debounces, and issues nothing when the value did not change (ADR-0016) |
| `UserFormPage` | a `409 RESOURCE_MODIFIED` on save shows a reload prompt and does not silently discard the edit (ADR-0013) |
| `UserFormPage` | `rowVersion` round-trips through the form model and is sent on update |
| `ProfilePage` | submits only name and email; there is no role control in the DOM |
| `ChangePasswordPage` | password-policy validation and mismatch handling |
| `HasRoleDirective` | renders for matching roles, removes the element otherwise |
| `LocaleService` | switching to Arabic sets `dir="rtl"` and `lang="ar"` on `<html>` and back again |
| i18n catalogues | `en.json` and `ar.json` have identical key sets |
| `ConfirmDialog` | returns true only on confirm; focus is trapped and restored to the trigger on close |

## 4b. Playwright suite — 83 tests over eight specs

Not an end-to-end framework. Every test here answers a question no cheaper layer can: component tests mock
HTTP, API tests have no browser, and neither can measure a focus ring or a horizontal overflow. Scope and
guardrails are fixed by ADR-0015.

| Spec | Tests | Proves |
|---|---|---|
| `admin-login.spec.ts` | 3 | Admin signs in and lands on the user list; a refused sign-in appears inline; the session survives a reload |
| `admin-creates-user.spec.ts` | 3 | Create form validates, submits, and the new user appears in the list |
| `user-list-query.spec.ts` | 3 | Search, role filter, column sort and page navigation each change the URL and the rendered rows |
| `readonly-cannot-mutate.spec.ts` | 2 | `ReadOnlyUser` sees no create/edit/delete controls, and a direct API call from the page context returns `403` |
| `arabic-rtl.spec.ts` | 4 | Arabic sets `dir="rtl"`, translates the shell and the tab title, survives a reload, and the table still lays out |
| `accessibility.spec.ts` | 27 | 16 page states scanned with axe-core in both directions, plus keyboard, focus, live-region and table-semantics behaviour axe cannot check ([16-accessibility-audit.md](16-accessibility-audit.md)) |
| `responsive.spec.ts` | 36 | Four viewports from 375x812 to 1440x900: no page overflow, nothing outside the viewport, navigation reachable, table scrolling in its own container, forms submittable, dialogs contained, Arabic intact |
| `assets.spec.ts` | 5 | The page loads nothing from a third-party origin; Material icons render as glyphs rather than as the words "visibility" and "delete"; and - behind nginx only - the root, a deep link, and API responses each carry exactly the headers they should, with no duplicated policy |

Guardrails: Chromium only, no visual snapshots, no page-object layer beyond a login helper, test data seeded
through the API rather than clicked in, and a failing spec is fixed or deleted — never retried into green.

Two rules learned the hard way, both from defects this suite found in itself. **No test may spend a shared
account's lockout budget** - the wrong-password test locks a throwaway account, because locking `admin` breaks
37 other tests on the fifth run inside fifteen minutes. And **the suite runs against the containerised
production build as well as the dev server**, because the dev server is always on localhost, localhost is
always a secure context, and the difference hid a defect that made the application unusable anywhere else.

## 5. Edge cases from the assignment, mapped to tests

| Edge case | Test |
|---|---|
| Duplicate username | `CreateUser_with_duplicate_username_throws_conflict` + integration `409` |
| Duplicate email | `CreateUser_with_duplicate_email_throws_conflict` + integration `409` |
| Invalid login | `Login_with_wrong_password_...` |
| Deleted account login | `Deleted_user_cannot_log_in` |
| Deleting an already-deleted user | `DeleteUser_already_deleted_throws_conflict` |
| Updating a nonexistent user | integration `PUT /api/users/{randomGuid}` -> `404` |
| Unauthorized update | authorization matrix theory |
| Forbidden role change | `Admin_changing_own_role_returns_422` |
| Invalid role | `CreateUser` with `roleId = 99` -> `400` |
| Empty search | `search=""` behaves as absent, returns page 1 |
| Invalid page number | `pageNumber=0` -> `400`; `pageNumber=9999` -> empty page |
| Excessive page size | `Page_size_101_returns_400` |
| Invalid sorting field | `Unknown_sort_field_returns_400_with_INVALID_SORT_FIELD` |
| Arabic localization | `Validation_errors_are_Arabic_...` + `LocaleService` direction test |
| No users found | `UsersListPage` empty-state test |
| Database failure | handler test with a repository throwing `DbUpdateException` -> `500` with only a trace id |
| Expired JWT | `Expired_token_returns_401` |
| Malformed JWT | `Malformed_token_returns_401` |

## 6. Commands

```bash
dotnet test                                            # 114 unit + 158 integration
dotnet test tests/UserManagement.UnitTests             # fast loop, no Docker needed
dotnet test --collect:"XPlat Code Coverage"            # coverage for the report
npm test --prefix frontend                             # 81 Vitest tests, watch off in CI
npm run lint --prefix frontend                         # ESLint: boundaries, sanitizer ban, template a11y + i18n
npm run lint:verify --prefix frontend                  # proves each of those rules actually fires
npm run lint:styles --prefix frontend                  # no physical left/right in any stylesheet
npm run e2e --prefix frontend                          # 84 Playwright tests, Chromium, API + SPA must be up
```

Or entirely in containers, with no SDK, Node or SQL Server installed:

```bash
docker compose --profile test run --rm --build backend-tests     # 114 unit + 158 integration
docker compose --profile test run --rm --build frontend-tests    # lint, rule checks, 81 Vitest tests
docker compose --profile e2e  run --rm --build e2e               # 84 Playwright tests against the built SPA
```

`--build` matters: Compose builds a missing image but never rebuilds a stale one, so omitting it silently runs
the previous commit's code.

The container path is not a convenience wrapper around the same run. `backend-tests` uses the
`USERMANAGEMENT_TEST_SQL` fallback against a second SQL Server service rather than Testcontainers, so the
Docker socket stays out of the test container - and the browser suite runs against the **production** bundle
behind nginx rather than the dev server, which is how it caught a defect that only appears outside a secure
context (see [15-final-review.md](15-final-review.md) section 5).

## 7. What is not tested, and why

- **No deep end-to-end suite.** The browser suite grew during hardening from five smoke specs to 84 tests, but
  the added weight is all in accessibility (27) and responsive layout (36) - two things only a real browser can
  answer. Behaviour stays where it is cheaper and steadier: component tests over a mocked HTTP layer, and
  integration tests against real SQL Server. Deep flows (every validation message, every error state, every role
  on every screen) are deliberately not duplicated in the browser.
- **No load or performance testing.** No performance NFR is stated. The query design in
  [05-database-model.md](05-database-model.md) addresses the assignment's performance requirements
  structurally (SQL-side filtering, covering indexes, projections, `AsNoTracking`), and one integration test
  asserts filtering is translated to SQL rather than evaluated in memory.
- **No mutation testing.** Valuable, but out of proportion to a module this size.
