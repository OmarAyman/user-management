# Final review

Executed after implementation, against the original brief. Everything here was run, not inspected.

**Verification run:** .NET 10.0.111, Node 24.19.0, SQL Server 2022 (local + Testcontainers), Chromium via
Playwright 1.62.1.

```text
dotnet build                    0 warnings, 0 errors      (warnings-as-errors enabled)
dotnet test                     99 unit + 145 integration passed
npm test --prefix frontend      55 passed
npm run e2e --prefix frontend   14 passed
```

Total: **313 automated tests, all passing.**

---

## 1. Quality gate

The brief's 41-item gate, with what proves each line.

| # | Item | Result | Evidence |
|---|---|---|---|
| 1 | Backend builds | Pass | `dotnet build`: 0 warnings, 0 errors, warnings-as-errors on |
| 2 | Frontend builds | Pass | `npm run build`: bundle generated, 338 kB initial |
| 3 | Database migrations work | Pass | `dotnet ef database update` applied; `SchemaTests` asserts the resulting shape |
| 4 | SQL scripts exist | Pass | `database/001_schema.sql` (generated), `002_seed.sql`, `003_sample_queries.sql` |
| 5 | Seed data exists | Pass | 3 demo accounts + 24 sample users; verified by query and by `SchemaTests` |
| 6 | Admin login works | Pass | `All_three_demo_accounts_can_sign_in`; also in the browser |
| 7 | User login works | Pass | same theory |
| 8 | ReadOnlyUser login works | Pass | same theory |
| 9 | Passwords securely hashed | Pass | PBKDF2-HMAC-SHA512 via `PasswordHasher<T>`; `PasswordHasherTests`; DB query shows `AQAAAAIAAYag...` |
| 10 | JWT authentication works | Pass | `TokenValidationTests`; malformed and expired tokens both `401` |
| 11 | Role authorization works | Pass | `AuthorizationMatrixTests` - every mutating endpoint x 3 roles |
| 12 | Admin CRUD works | Pass | `UserCrudTests`; and end to end in the browser |
| 13 | Profile update works | Pass | `ProfileTests.Every_role_can_read_and_update_its_own_profile` |
| 14 | Users cannot change their role | Pass | `An_admin_cannot_change_their_own_role` (422); profile model has no role field |
| 15 | ReadOnlyUser cannot mutate | Pass | matrix theory + `readonly-cannot-mutate.spec.ts` calling the API directly |
| 16 | Search works | Pass | `Search_matches_username_email_first_name_and_last_name` |
| 17 | Pagination works | Pass | `A_page_carries_items_and_honest_metadata`, `Paging_does_not_repeat_or_drop_rows_across_pages` |
| 18 | Sorting works | Pass | every whitelisted field, both directions |
| 19 | Role filtering works | Pass | `The_role_filter_returns_only_that_role` |
| 20 | Soft delete works | Pass | `SoftDeleteTests` + `UserCrudTests`; no hard-delete path exists |
| 21 | Deleted users excluded correctly | Pass | `Deleted_user_is_absent_from_the_list_...`; `Deleted_route_returns_403_for_user_and_readonly` |
| 22 | Audit trail works | Pass | `A_full_lifecycle_produces_the_expected_actions_against_the_users_immutable_id` |
| 23 | IP address captured | Pass | `Audit_row_records_the_target_id_display_name_ip_and_correlation_id` |
| 24 | Created/modified timestamps work | Pass | interceptor; asserted in `UserCrudTests` (`createdBy`, `lastModifiedBy`) |
| 25 | Failed logins logged | Pass | `LoginFailed` with a failure category; never the password |
| 26 | Role changes logged | Pass | dedicated `RoleChange` audit row **and** a `RoleChanged` log event |
| 27 | Deletions logged | Pass | `Delete` audit row + `UserDeleted` log event |
| 28 | Structured errors returned | Pass | `Error_responses_are_ProblemDetails_with_errorCode_and_traceId` |
| 29 | No stack traces leak | Pass | `Unhandled_exception_returns_500_without_detail` |
| 30 | English localization works | Pass | default culture; `The_same_failure_is_english_by_default` |
| 31 | Arabic localization works | Pass | `An_authentication_failure_is_arabic_under_accept_language_ar`; resx parity test |
| 32 | RTL works | Pass | `arabic-rtl.spec.ts`; `dir="rtl"`, Arabic headers, table intact |
| 33 | Swagger works | Pass | UI returns 200; 13 paths; Bearer scheme present |
| 34 | Postman collection exists | Pass | `postman/` collection + environment, with assertions |
| 35 | Unit tests pass | Pass | 99 |
| 36 | Integration tests pass | Pass | 145, against real SQL Server |
| 37 | Angular tests pass | Pass | 55 Vitest + 14 Playwright |
| 38 | README complete | Pass | all 17 required sections |
| 39 | No secrets committed | Pass | `git grep` for key patterns; only documented demo passwords and PBKDF2 hash literals |
| 40 | Git history meaningful | Pass | 20 commits, one coherent slice each, each explaining *why* |
| 41 | No obvious vulnerabilities remain | Pass | `dotnet list package --vulnerable` clean; `npm audit` clean (see section 3) |

## 2. Requirements traceability

The full matrix is [01-requirements-checklist.md](01-requirements-checklist.md), updated with a status per row.
Summary by area:

| Area | Requirements | Verified | Notes |
|---|---|---|---|
| Architecture and code organisation | 10 | 10 | Layering asserted by three architecture tests |
| Domain and database | 13 | 13 | Includes concurrency token and token families |
| Authentication and session | 11 | 11 | |
| Authorization | 9 | 9 | Matrix is an executable theory |
| User management and list behaviour | 15 | 15 | |
| Audit, logging, observability | 13 | 13 | Audit policy is normative and enforced by 12 tests |
| API design, errors, validation | 8 | 8 | |
| Localization and UX | 12 | 12 | |
| Security review | 7 | 7 | section 3 below |
| Testing, documentation, delivery | 13 | 13 | |

Nothing is marked verified without a named test or a recorded run.

## 3. Security review

Against the categories the brief lists.

| Category | Finding |
|---|---|
| Authentication | PBKDF2-HMAC-SHA512, no hand-rolled cryptography. Unknown user, wrong password and soft-deleted account are indistinguishable; verification always runs so timing does not separate them. `ACCOUNT_LOCKED` only after a correct password. |
| Authorization | Server-side policies, executable matrix, three roles. `ReadOnlyUser` and `User` receive `403` on every admin route, proven both from tests and from a browser bypassing the UI. |
| IDOR | `/users/me` has no id segment; `/users/{id}` is Admin-only. No code path lets client input select the row being written. |
| Mass assignment | Scoped request records plus `UnmappedMemberHandling.Disallow`: the payload the brief names returns `400`. |
| Password storage | Hash only, never returned, never logged, redacted in audit rows. Plaintext exists only as a request field. |
| JWT security | 15-minute lifetime, `ClockSkew.Zero`, issuer/audience/signature validated, four claims, unmapped. |
| Secret management | Nothing real committed; startup guard refuses a placeholder key outside Development. |
| SQL injection | EF Core parameterisation; sorting selects a branch from a whitelist. `?sortBy=id;DROP TABLE Users` returns `400`. |
| XSS | Angular contextual escaping; no `bypassSecurityTrust*` anywhere; CSP and `nosniff` headers on API responses. |
| CSRF | Bearer header for the API (a cross-site form cannot set it); refresh cookie is `httpOnly`, `SameSite=Strict`, path-scoped, and CORS names explicit origins. |
| Information disclosure | Generic auth failures, generic `500` with only a trace id, `Server` header suppressed, Swagger off outside Development. |
| Logging of sensitive data | No code path passes a credential to a logger, and request bodies are not logged. Asserted by `SensitiveDataLoggingTests`, which runs four sign-in outcomes and a password change through a capturing logger and checks structured values as well as rendered text. |
| Soft-delete bypass | Global filter; `IgnoreQueryFilters()` in one file, three justified callers, no client-controlled parameter. |
| Role escalation | Nobody changes their own role; the last administrator cannot be removed or demoted; role changes revoke sessions. |

### Dependency scans

```text
dotnet list package --vulnerable --include-transitive   no vulnerable packages (6 projects)
npm audit --omit=dev                                    0 vulnerabilities
npm audit (including dev)                               0 vulnerabilities
```

`npm audit` initially reported two high-severity advisories in the Playwright dev dependency. Resolved by
updating `@playwright/test` to 1.62.1; the browser suite was re-run afterwards and still passes 14/14.

`dotnet list package --deprecated` flags **xunit 2.9.3** as legacy in favour of xunit.v3. Not a
vulnerability and not addressed: migrating the test framework at the end of the project would risk 244 working
tests for no functional gain. Recorded as a known limitation instead.

### Residual risks

Carried from [08-security-plan.md](08-security-plan.md), all deliberate:

1. An issued access token remains valid for up to 15 minutes after sign-out, deletion or a role change.
   Refresh is revoked immediately, so the session cannot be extended.
2. An XSS foothold could still act as the user while the page is open. The in-memory token prevents theft for
   later use, not abuse in the moment.
3. An operator with direct SQL access can alter audit rows. Mitigation is database permissions, stated as a
   deployment requirement.
4. Audit rows contain personal data and no retention policy is implemented - that is an operator decision.
5. Search uses a leading wildcard and cannot seek; covering indexes keep it cheap at this scale.

## 4. Reviewer pass: what was found and fixed

Acting as a strict reviewer over the whole implementation. Each item below was found *during* the work and
fixed, not left as a note.

| # | Found | Fix |
|---|---|---|
| 1 | Two indexes declared over `Username` without explicit names: EF treated the second as a reconfiguration of the first, silently renaming the filtered unique index and never creating the login index. The model looked correct; only the DDL was wrong. | Named both. `SchemaTests` now asserts index metadata directly - the class of defect a behavioural test misses. |
| 2 | Audit JSON keys came out PascalCase: `Dictionary` keys are governed by `DictionaryKeyPolicy`, not `PropertyNamingPolicy`. | Configured, with a test. |
| 3 | Angular HTTP interceptors registered in the intuitive order. Responses travel back through the array in reverse, so the refresh interceptor saw the raw `HttpErrorResponse` before the mapper, fell into its defensive branch, and attempted a refresh after a genuinely wrong password. | Reordered, documented as a rule, and covered by a test that fails if it regresses. |
| 4 | Validators written against internal commands would never have run - the action filter validates the request record MVC bound. Validation that looks present and provides nothing. | Moved to the transport boundary; recorded as ADR-0019. |
| 5 | `OrderBy` over a constructor-projected record cannot be translated by EF Core; it throws at runtime. | Sort on the entity before projecting, which also keeps the `ORDER BY` on indexed columns. |
| 6 | `BadRequestException` was introduced without a mapping, so an unknown sort field returned `500` instead of `400`. | Mapped, with tests for three injection-shaped inputs. |
| 7 | The password-visibility button carried `aria-label="Password"`, colliding with the input's accessible name. A screen reader would announce two controls with the same name, neither describing the button. | Now "Show password" / "Hide password", in both languages. |
| 8 | At 375px the toolbar was wider than the viewport, so the page scrolled sideways. | Navigation collapses into a menu below 768px. The table was already correct - it scrolls inside its own container. |
| 9 | Test harness pointed at the developer's local database because WAF config overrides were applied too late, and every test still passed. | Overrides now go through `UseSetting`, and the fixture asserts which database it connected to. |
| 10 | Two localization tests probed wrong passwords against the shared `jdoe` account, locking it for the whole suite. | Failure paths use throwaway accounts; the reason is recorded in the file. |
| 11 | `sqlcmd` defaults `QUOTED_IDENTIFIER` off, so SQL Server refused to create - or insert into - a table with a filtered index. The generated script failed outside SSMS. | The generator prepends the required `SET` options; all three scripts verified against a fresh database. |
| 12 | A test asserted `securityStamp` never appears in an audit row, contradicting the documented policy that it is *redacted*. | Test corrected to assert the policy: the key may appear, carrying only `"***"`. |
| 13 | A destructive integration test reduced the system to one administrator, which would have broken every other test sharing the database. | Removed; BR-02 and BR-03 are unit-tested where the admin count can be controlled. |

| 14 | The security plan claimed a Serilog destructuring policy and a redaction test that did not exist - the code happened not to log credentials, but nothing enforced it. | Wrote `SensitiveDataLoggingTests` (five cases, checking structured values as well as rendered text) and corrected the wording in the README and the security plan to describe what is actually in place. |
| 15 | Two documents were corrupted by a PowerShell encoding round-trip, which mangled every em dash and ellipsis. | Repaired, and those files now use ASCII punctuation so the failure cannot recur. The Arabic resource and translation files were checked too and are intact - they are verified independently by the parity tests. |

Also checked and found clean: no `TODO`, no `NotImplementedException`, no commented-out code, no
`async void`, every `async` method taking a `CancellationToken` on the request path, no magic strings for
roles or error codes, and no controller containing business logic or a `try/catch`.

One environment quirk, not a defect: Vitest spawns a worker per spec file, and lingering .NET test hosts from
a `dotnet test` run immediately before can leave too little headroom, which surfaces as
`Failed to start forks worker`. Running the two suites as separate steps is reliable, and the README says so.

## 5. Design documents corrected by implementation

The Phase 1 documents were written before the code and were wrong in places. Rather than quietly editing
them, the corrections are recorded:

| Document | Correction |
|---|---|
| 02-architecture | Interceptor ordering rationale was inverted (see section 4.3). Validation location moved to the API layer. Two exception handlers, not eight. |
| 02-architecture / 07-authorization | The soft-delete opt-out has three justified consumers, not two. |
| 05-database-model | Identifiers are client-generated version 7 GUIDs, not `NEWSEQUENTIALID` defaults - which is what allows audit rows to be written inside the same transaction. Roles are seeded by the migration, not the seeder. |
| 03-project-structure | `.slnx`, `NuGet.config`, real file names, `TestSupport` naming, `IUnitOfWork` implemented by the DbContext. |
| 12-decision-log | Four ADRs added during implementation (0018-0020 plus the ADR-0009 reversal), each with the reasoning that produced it. |

## 6. What a reviewer should look at first

If time is short, these five files carry most of the reasoning:

1. `src/UserManagement.Application/Features/Auth/Login/LoginCommandHandler.cs` - the four-outcome sign-in
   table and why only one outcome is distinguishable.
2. `tests/UserManagement.IntegrationTests/Users/AuthorizationMatrixTests.cs` - the brief's matrix as a test.
3. `src/UserManagement.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs` - why auditing
   cannot be forgotten.
4. `frontend/src/app/app.config.ts` - the interceptor ordering rule and the defect behind it.
5. `docs/12-decision-log.md` - 20 decisions with their alternatives and costs, including the one that was
   reversed.




