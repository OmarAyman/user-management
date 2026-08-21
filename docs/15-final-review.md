# Final review

Executed after implementation, against the original brief. Everything here was run, not inspected.

**Verification run:** .NET 10.0.111, SQL Server 2022 (local + Testcontainers), Chromium via Playwright 1.62.1.
Node 24.19.0 for the implementation phases; the submission-hardening pass re-ran everything on Node 25.9.0 as
well, which is how the storage-globals problem in section 6 came to light.

```text
dotnet build                       0 warnings, 0 errors      (warnings-as-errors enabled)
dotnet test                        114 unit + 153 integration passed
npm test --prefix frontend         75 passed
npm run e2e --prefix frontend      83 passed
npm run lint --prefix frontend     0 problems
npm run lint:verify --prefix frontend   9 lint-rule checks passed
npm run lint:styles --prefix frontend   no physical direction properties
newman run postman/...             42 requests, 66 assertions, 0 failures (run twice)
docker compose up --build          three services healthy; all three suites also run in containers
```

Total: **425 automated tests, all passing**, plus 66 Postman assertions and 9 lint-rule checks.

---

## 1. Quality gate

The brief's 41-item gate, with what proves each line.

| # | Item | Result | Evidence |
|---|---|---|---|
| 1 | Backend builds | Pass | `dotnet build`: 0 warnings, 0 errors, warnings-as-errors on |
| 2 | Frontend builds | Pass | `npm run build`: 355 kB initial (24 kB of it CSS, up from 9 kB now that the fonts are bundled rather than fetched from Google); also built and served from the `web` container |
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
| 34 | Postman collection exists | Pass | 8 folders, 42 requests, 66 assertions; run through newman twice with zero failures |
| 35 | Unit tests pass | Pass | 114 |
| 36 | Integration tests pass | Pass | 153, against real SQL Server |
| 37 | Angular tests pass | Pass | 75 Vitest + 83 Playwright, the browser suite run against both the dev server and the containerised production build |
| 38 | README complete | Pass | all 17 required sections |
| 39 | No secrets committed | Pass | `git grep` for key patterns; only documented demo passwords and PBKDF2 hash literals |
| 40 | Git history meaningful | Pass | one coherent slice per commit, each explaining *why*; no history rewritten during hardening |
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
| Localization and UX | 12 | 12 | Accessibility and responsive layout moved from `Partial` to `Verified` in the hardening pass, on the strength of 63 browser tests |
| Security review | 7 | 7 | section 3 below |
| Testing, documentation, delivery | 15 | 14 | The one exception is the demo **recording** (J-15): the script is written and every screen it calls for works, but no video is in the repository |

Nothing is marked verified without a named test or a recorded run, and the one thing that is missing is listed
as missing rather than dropped from the matrix.

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
updating `@playwright/test` to 1.62.1; the browser suite was re-run afterwards and passes.

The hardening pass added five dev dependencies (`eslint`, `angular-eslint`, `typescript-eslint`,
`@eslint/js`, `eslint-plugin-import-x`) to implement the boundary enforcement the architecture documents already
claimed. `npm audit` was re-run afterwards: **0 vulnerabilities**. ESLint 9 was installed first and npm reported
it as no longer supported; it was replaced with 10.8.1 rather than left on an unsupported line.

`dotnet list package --deprecated` flags **xunit 2.9.3** as legacy in favour of xunit.v3. Not a
vulnerability and not addressed: migrating the test framework at the end of the project would risk 267 working
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

## 5. Submission-hardening pass

A second review pass over the finished repository, with a deliberately narrow mandate: fix genuine defects,
verify what was claimed but unproven, correct documentation that did not match reality, and change nothing else.
No architecture was redesigned, no framework replaced, no history rewritten.

### Defects found and fixed

| # | Found | Fix |
|---|---|---|
| 16 | **Documented-but-absent frontend boundaries.** 02-architecture and 03-project-structure both stated that the four boundary rules were enforced by ESLint so a violation "fails CI rather than a review". There was no ESLint in the project at all: no config, no dependency, and `npm run lint` failed with *Cannot find "lint" target*. | Implemented what was documented rather than softening it: `eslint.config.js` with `import-x/no-restricted-paths` for all four rules, plus template accessibility and i18n rules. |
| 17 | **The boundary rules were silently inert.** Once installed, `eslint .` reported zero problems - and so did a deliberate `core -> features` import. Without a TypeScript-aware resolver no extensionless relative import resolved, and unresolved imports pass. A green lint run proved nothing. | Added the resolver, and wrote `scripts/verify-lint-rules.mjs`: nine deliberate violations, one per rule, plus a negative control. It found the problem and now prevents it recurring. It also immediately caught a spec of mine in `core/i18n/` importing `features/users`. |
| 18 | **Browser tab titles were never localized.** 09-localization-plan claimed page titles were localized "via a title strategy". No strategy existed; routes carried literal English (`title: 'Users'`), so every tab title stayed English in an Arabic session. The only user-visible text outside a template, and the only text the localization pass missed. | `TranslatedTitleStrategy` plus route translation keys, reusing the keys the pages already display so a title cannot drift from its page. Five unit tests, one browser test, and `route-titles.spec.ts` asserting all seven keys resolve in both catalogues. |
| 19 | **The first title strategy showed the raw key.** Translating on `langChanges$` answers with the key, because the new catalogue has not loaded when the language changes. The tab read `users.list.title` until the next navigation. The unit test had preloaded both catalogues and passed; a browser test caught it. | Rewritten over `selectTranslate`, which waits for the catalogue and re-emits per language. The unit spec now uses a deliberately slow loader, and one test pins the exact regression: the previous title stays until the new one is ready. |
| 20 | **Twelve tests failed on Node 25.** From Node 25 the runtime exposes its own Web Storage on the global the jsdom environment shares, and without `--localstorage-file` it is a stub whose methods are `undefined`. The failures read `localStorage.clear is not a function` - blaming the specs for a runtime difference. A reviewer on a current Node would have seen a red suite. | `src/test-setup.ts` uses the environment's storage when it works and substitutes a real in-memory `Storage` when it does not. Both branches are tested, because on any one machine only one of them runs. |
| 21 | **Vitest failed under load.** `Failed to start forks worker`, three times, with the dev server or a .NET test host running - a resource limit that reads like catastrophic breakage. Previously documented as a quirk to work around by running the suites separately; that is a workaround, not a fix. | `vitest.config.ts` caps the worker pool. Switching to the threads pool was tried first and was wrong - it broke the browser globals - which is recorded in the file so it is not retried. |
| 22 | **Two more documented-but-absent enforcement claims.** 09-localization-plan claimed a lint rule for literal text in templates and a stylelint rule banning physical `left`/`right`. Neither existed, though the codebase happened to comply with both. | `@angular-eslint/template/i18n` with `checkText` now makes literal text an error (`index.html` exempted, with the reason stated), and `scripts/verify-logical-properties.mjs` checks `.scss` and inline styles - verified by feeding it a violation, not just by watching it pass. |
| 23 | **`X-Forwarded-For` was documented as handled and was not.** The audit trail records a client IP and lockout counts per account, so a forged header would put attacker-chosen addresses into audit rows that look authoritative. | Opt-in `ForwardedHeaders` configuration: the header is ignored unless a deployment names the proxies it trusts, ASP.NET Core's default loopback trust is cleared rather than inherited, and the do-nothing path now says so at startup. Six unit tests and two integration tests. |
| 24 | **The Postman collection was broken for a reviewer.** The environment's empty `accessToken` shadowed the value captured at runtime, so every authenticated request returned 401; the role-demo logins hijacked the admin session; a password-change request left the demo account altered for the next run; and a User-role profile update reused another user's `rowVersion`. | Restructured into 8 ordered folders with runtime capture, and reduced the environment to `baseUrl` alone. 42 requests, 66 assertions, run twice with zero failures. |
| 25 | **The global focus ring never rendered on any button.** Material sets `outline: none` and its styles are injected after the global stylesheet, so a bare `:focus-visible` rule lost on equal specificity. The documented focus indicator did not exist on the controls users tab through most. | Element-qualified selectors, no `!important`. The test measures computed outline rather than trusting the stylesheet. |
| 26 | **Accessibility and responsive layout were claimed on manual checks alone**, and honestly carried as `Partial`. | 27 axe-driven accessibility tests over 16 page states in both directions, and 36 responsive tests across four viewports. Both rows are now `Verified`, with the six areas still untested listed explicitly in [16-accessibility-audit.md](16-accessibility-audit.md). |

Nine of these eleven are the same species: **something true in the code but unenforced, or something claimed in
a document and absent from the code.** That is what a hardening pass is for. The two exceptions - the raw-key
title and the focus ring - were behavioural defects a user would have met.

### Then the whole thing was run in Docker

The compose file shipped SQL Server and the API and said the SPA "is still run with npm". Containerising the
rest, so a reviewer needs Docker and nothing else, turned up four more defects - including the most serious one
in the project.

| # | Found | Fix |
|---|---|---|
| 27 | **`crypto.randomUUID` is not available outside a secure context, and the correlation-id interceptor called it on every request.** Served over plain http from anything other than localhost - a container hostname, an IP address, an internal staging box - `randomUUID` is `undefined`, so the interceptor threw on the *first* request the application makes, which is Transloco fetching its translation catalogue. The result was a blank page and "Unable to load translation and all the fallback languages", with nothing pointing at an interceptor. The application was, in plain terms, dead anywhere but localhost and HTTPS. Local development structurally cannot reveal this, because localhost is a secure context by definition | Use `randomUUID` when it exists, otherwise build a v4 from `getRandomValues` - which is *not* secure-context gated - and fall back to `Math.random` as a last resort. A correlation id names a request in a log; it is not a secret, and a weak id beats an application that will not start. Three tests pin all three paths |
| 28 | **The API image had never been built.** `RUN adduser` does not exist in the .NET 10 runtime image, so `docker compose up --build` failed on the first attempt anyone made. The traceability matrix carried the row as Verified. The `HEALTHCHECK` was equally untested: it used `wget`, also absent | The base image already provides a non-root `app` user as `$APP_UID`, so there is nothing to create. `curl` is installed for the health check rather than dropping the check - a container permanently reported unhealthy while actually serving teaches an operator to ignore health |
| 29 | **The documented no-Docker test fallback shared one database between two fixtures.** `USERMANAGEMENT_TEST_SQL` was read independently by the API fixture and the persistence fixture, and xUnit runs their collections in parallel - so both migrated the same database and then interleaved writes across the same tables. On the Testcontainers path each fixture gets its own container, which is why this never showed. 122 of 153 tests died with "A severe error occurred on the current command", which reads like a broken SQL Server | One database per fixture, derived from whatever name is configured. The fallback is what a container run uses, so this had to be right before the suites could run in Docker at all |
| 30 | **A browser test spent the shared admin account's lockout budget.** The wrong-password test signed in as `admin`; five failures inside fifteen minutes lock the account. One suite run is fine, so it passed for weeks. Against a long-lived containerised stack, repeated runs locked admin out and 37 tests went red at once with nothing in the failures naming the cause - the same species as the localization-test defect in item 10 | The test creates a throwaway account and locks that instead. The assertion is unchanged, because what it proves is the interface's behaviour on a refused sign-in |

| 31 | **The SPA carried no security headers.** The API set a CSP, `X-Frame-Options`, `nosniff` and `Referrer-Policy` on its JSON - the one response that cannot execute script - while the document that can had none, and 08-security-plan named CSP as an XSS control. Serving the bundle from nginx made that visible: `curl -I` on the page returned nothing but `Server: nginx/1.29.8` | A CSP on the document with `script-src 'self'` and no exemption, plus the three other headers and `server_tokens off`. Repeated in each location that sets its own `add_header`, because nginx discards inherited headers there - a subtlety that silently drops them from exactly the cacheable responses |
| 33 | **The second `docker compose up` against an existing volume crashed the API.** A SQL Server container answers `SELECT 1` - and so passes its health check - before it has finished bringing user databases online after a restart. EF Core asked whether the database existed, was told it did not, issued `CREATE DATABASE` and got error 1801: it already does. The process died on startup, so a reviewer who stopped and restarted the stack got a dead API where the first run had worked | `StartupRetry` makes the migration and the seeder retryable: five bounded attempts, logged at `Warning` because a dependency still starting is expected rather than wrong, and the process is still allowed to die if it never succeeds. A stronger health check was the alternative and is weaker in principle - it can only say the engine was ready a moment ago, and the same race exists for a managed database that fails over during startup. Five unit tests, with the delay injected so they do not spend fifteen seconds proving it. The failing sequence was reproduced, then re-run after the fix and came up healthy |
| 32b | **The document policy would have been appended to API responses too.** Set on the nginx `server` block, `add_header` applies to every location that does not declare its own - so each proxied API response carried both its own `default-src 'none'` and the page's policy, and two `X-Frame-Options` headers. Browsers intersect multiple CSPs, and some ignore a duplicated `X-Frame-Options` outright | Headers moved to the locations that serve the document. The catch-all location needs its own copy, because `try_files` rewrites to index.html without re-entering the `= /index.html` location - miss that and every deep link is served with no policy while `/` looks protected. Two tests: one on a deep link, one asserting API responses carry at most one policy |
| 32 | **Roboto and the Material icon font were fetched from Google.** On any network that cannot reach `fonts.gstatic.com` - a restricted corporate network, an air-gapped review, the e2e container - every icon rendered as its own ligature text: the toolbar read "visibility", "delete", "edit". Not a degraded interface, a broken one. It also forced two third-party origins into the CSP | Both fonts bundled through `@fontsource`, the Google links removed from `index.html`, and the `.mat-icon` font-family declared by hand - Google's stylesheet provided that class, and a bundled `@font-face` does not. Angular's `inlineCritical` optimization is off, because it rewrites the stylesheet link with an inline `onload` handler that a strict `script-src` blocks. The page now loads **zero** third-party resources, verified by a test that fails if any appear |

Three of the suite's own assumptions had to be corrected along the way, and they are worth naming because each
was hiding behind a green run. The keyboard-only sign-in test started tabbing as soon as `goto` resolved, which
is the load event and not the point at which Angular has rendered a form - harmless while the stylesheet was
small, a failure once bundled fonts moved first paint later, and it would have read as a broken sign-in rather
than as a test racing the application. The third-party-request test compared each request against `page.url()`
inside the handler, where the page is still on `about:blank`, so it counted the application's own document as
external. And the icon test trusted `document.fonts.ready`, which can resolve before a lazily fetched face
arrives; it now asks for the face explicitly. None of the three were retried into green.

Item 27 is the strongest argument in this project for running software the way it will actually be run. Every
suite was green, 412 tests including 78 in a real browser, and the application would not have started for
anyone who deployed it to an internal host over http. Items 28 to 32 are all of the same family: each was
invisible until the application was built, served and driven the way a deployment would.

### Verified in containers

```text
docker compose up --build                                          three services healthy; sign-in on :4200
docker compose --profile test run --rm --build backend-tests       114 unit + 153 integration passed
docker compose --profile test run --rm --build frontend-tests      lint clean, 9 rule checks, 75 Vitest
docker compose --profile e2e  run --rm --build e2e                 83 Playwright passed
```

`--build` is part of the command rather than a flourish: Compose builds a missing image and never rebuilds a
stale one, so a run without it tests whatever the image was last built from. It reported 75 Vitest tests as 72
once, which is how that got noticed.

The SPA is served as a production bundle behind nginx with `/api` proxied through the same origin, so the
httpOnly refresh cookie works without CORS credentials - the containerised stack exercises the same
single-origin arrangement the dev-server proxy gives locally, rather than a different one.

### What was deliberately not changed

- **No vulnerability was invented.** The security review found no new exploitable defect; the forwarded-header
  work closed a real gap in a documented control, and everything else above is correctness or reproducibility.
- **xUnit was not migrated**, the architecture was not redesigned, and no framework was replaced.
- **No commit was rebased, squashed or amended**, and no `Co-Authored-By` trailer was removed. The hardening
  work is new commits on top.
- **No documentation was weakened to match a missing implementation.** Every claim above was resolved by
  writing the implementation, except two where the documented *mechanism* was wrong for this stack: the literal
  text rule (Transloco, not Angular `i18n` attributes) and the style check (a repository script, not stylelint).
  In both cases the requirement was implemented and the mechanism named accurately.

## 6. Design documents corrected by implementation

The Phase 1 documents were written before the code and were wrong in places. Rather than quietly editing
them, the corrections are recorded:

| Document | Correction |
|---|---|
| 02-architecture | Interceptor ordering rationale was inverted (see section 4.3). Validation location moved to the API layer. Two exception handlers, not eight. |
| 02-architecture / 07-authorization | The soft-delete opt-out has three justified consumers, not two. |
| 05-database-model | Identifiers are client-generated version 7 GUIDs, not `NEWSEQUENTIALID` defaults - which is what allows audit rows to be written inside the same transaction. Roles are seeded by the migration, not the seeder. |
| 03-project-structure | `.slnx`, `NuGet.config`, real file names, `TestSupport` naming, `IUnitOfWork` implemented by the DbContext. The frontend tree was redrawn in the hardening pass: it had listed folders and specs that never existed and omitted several that do. |
| 12-decision-log | Four ADRs added during implementation (0018-0020 plus the ADR-0009 reversal), each with the reasoning that produced it. |
| 09-localization-plan | Two enforcement mechanisms were named that did not exist, and the localized page titles were not implemented at all. All three are now real; see section 5, items 18 and 22. |
| 08-security-plan / README | The forwarded-header handling described in the threat model was not implemented. It is now, opt-in, with tests (section 5, item 23). |
| 12-decision-log | ADR-0012 described a Docker path that had been written and never executed - the API image did not build. The ADR now carries its revision: the SPA and all three suites are containerised too, and the reason that mattered is item 27, a defect only visible when the application runs somewhere other than localhost. |

## 7. What a reviewer should look at first

If time is short, these five files carry most of the reasoning:

1. `src/UserManagement.Application/Features/Auth/Login/LoginCommandHandler.cs` - the four-outcome sign-in
   table and why only one outcome is distinguishable.
2. `tests/UserManagement.IntegrationTests/Users/AuthorizationMatrixTests.cs` - the brief's matrix as a test.
3. `src/UserManagement.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs` - why auditing
   cannot be forgotten.
4. `frontend/src/app/app.config.ts` - the interceptor ordering rule and the defect behind it.
5. `docs/12-decision-log.md` - 20 decisions with their alternatives and costs, including the one that was
   reversed (ADR-0009) and the one that was revised because running it proved it wrong (ADR-0012).




