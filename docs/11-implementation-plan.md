# Implementation plan

Phase 1 (this document set) ends at a design review gate. Phases 2-10 each end with a **verification step**
whose output is pasted into the phase report, and a **commit**. Nothing moves forward on a red build.

## Prerequisite risks

| Risk | Detail | Action |
|---|---|---|
| Node.js version | Installed: **v25.9.0**. Angular 21 supports `^20.19 \|\| ^22.12 \|\| ^24`. Odd-numbered releases never reach LTS, and the CLI already prints a warning. | **Node 24 LTS is a hard prerequisite for Phase 7** (ADR-0017), pinned by `frontend/.nvmrc` containing `24` and a `package.json` `engines` field, and documented in the README as a requirement rather than a suggestion. Angular, Material and CDK all stay on the 21 major. |
| Docker for integration tests | Docker 29.7.2 present and the daemon responds. `mcr.microsoft.com/mssql/server:2022-latest` has been pulled locally, so Testcontainers starts without a first-run download. | Testcontainers is the default. A `USERMANAGEMENT_TEST_SQL` environment variable overrides it with a connection string, which is the documented fallback for a machine without Docker. |
| Local SQL Server | `MSSQLSERVER` and LocalDB both running. | Default dev connection targets `localhost` with Windows auth; Compose is the reviewer path. |
| Angular major | CLI 21.0.2 installed; Angular 22 exists. | Pin **21.x** in `package.json`. It satisfies the assignment's "21+", matches the installed CLI, and is the version whose zoneless and Vitest behaviour is verified here. |

## Phase 2 — Backend foundation

**Build:** solution, four projects, project references, `Directory.Build.props` (nullable, warnings-as-errors,
analyzers, latest lang version), `Directory.Packages.props`, `.editorconfig`; Domain entities, enums and
constants including `ErrorCodes`; `ApplicationDbContext` with entity configurations, **filtered unique
indexes**, the **`rowversion` concurrency token**, the global soft-delete query filter and both interceptors;
Application ports and common models; Infrastructure repositories with `QueryActive()` /
`QueryIncludingDeleted()`; password hashing, JWT issuing and refresh-token persistence with families;
strongly typed options with `ValidateOnStart`; Serilog; `InitialCreate` migration; `DbSeeder`; DI composition
per layer; `appsettings.example.json`; the authentication foundation wired into the API host.

**Verify:** `dotnet build` clean with zero warnings; unit tests green; integration tests green against real
SQL Server; `dotnet ef database update` creates the schema; the three roles and three seeded users exist
(query output pasted into the report); password hashes are not plaintext; the soft-delete filter, the
concurrency token and the filtered unique indexes each demonstrated by a test; audit interceptor registered
and writing rows; no secret committed.

**Commits:**
```text
chore: initialize solution structure and build configuration
feat: add domain entities, roles and audit enums
feat: add EF Core context, configurations, interceptors and initial migration
feat: add configuration options, serilog and dependency injection composition
feat: add idempotent database seeder with roles and demo users
```

## Phase 3 — Authentication

**Build:** `IPasswordHasher` over `PasswordHasher<User>`; login use case with lockout and dummy-hash timing
mitigation; `AccessTokenIssuer`; `RefreshTokenService` with rotation, reuse detection and revocation;
`RefreshTokens` migration; JWT bearer authentication with `ClockSkew = 0`; the two authorization policies;
`CurrentUserService` and `ClientInfoProvider`; auth rate limiting; `AuthController` (login, refresh, logout);
`appsettings.example.json` and the placeholder-key startup guard.

**Verify:** all three demo accounts log in and receive a token with exactly the four expected claims; a
soft-deleted account is refused; five bad passwords lock the account; refresh rotates the cookie; a reused
refresh token revokes the chain; expired and tampered tokens return `401`. Evidence: integration test output
plus a decoded token payload.

**Commits:**
```text
feat: add password hashing abstraction over ASP.NET PasswordHasher
feat: implement login with account lockout and failed-login logging
feat: implement JWT issuing and bearer authentication
feat: implement refresh token rotation with reuse detection
feat: add role based authorization policies and current-user services
```

## Phase 4 — User management

**Build:** every Users use case (list, get, create, update, delete, restore, me, update-me,
change-password); request records in `Api/Contracts`; FluentValidation validators and the validation filter;
`PagedResult<T>`; `SortFieldMap`; role filter; the Admin-only deleted-users route; the availability query;
business rules BR-01..BR-17 including the concurrency and restore-collision rules; `UsersController`,
`RolesController`.

**Verify:** the authorization matrix theory passes for all three roles; search/sort/paging/filter tests pass,
including the SQL-translation assertion; BR-01..BR-15 each have a green test; the captured SQL for a list page
is pasted into the report to show one round trip plus one count, no `SELECT *`, no client-side evaluation.

**Commits:**
```text
feat: add paged, searchable, sortable user listing with role filter
feat: implement admin user CRUD with DTO-scoped request models
feat: implement soft delete and restore with business rule guards
feat: implement self-service profile and password change
feat: add validation layer and last-admin protection rules
```

## Phase 5 — Audit trail

**Build:** `AuditLog` entity, configuration and migration; `AuditEntryBuilder` with the redaction list;
`AuditSaveChangesInterceptor` wired for before/after change capture; `RoleChange` delta detection;
correlation id in audit rows; `GetAuditLogs` query with filters; `AuditLogsController` (Admin only);
named security log events.

**Verify:** an insert, update, role change, delete and restore each produce the expected rows (table pasted
into the report); no audit row contains password material; audit rows are unchanged by later operations;
IP and correlation id are populated.

**Commits:**
```text
feat: add audit log entity and persistence configuration
feat: capture entity changes via save-changes interceptor with redaction
feat: add admin audit log query endpoint with filters
feat: log security events for login failure, role change and deletion
```

## Phase 6 — Error handling and localization

**Build:** typed Application exceptions; the `IExceptionHandler` chain; ProblemDetails factory with stable
`errorCode` and `traceId`; correlation-id middleware; security headers; request localization; `Messages.resx`
and `Messages.ar.resx`; localized validator messages; the resx parity test.

**Verify:** each error type returns the documented status, code and shape; a forced exception returns a `500`
carrying only a trace id, with the full detail present in the log; `Accept-Language: ar` returns Arabic
titles with unchanged codes; resx parity test green.

**Commits:**
```text
feat: add centralized exception handling with RFC 7807 problem details
feat: add correlation id middleware and security headers
feat: add backend localization for validation and business errors
```

## Phase 7 — Angular application

**Build:** workspace (`frontend/`), ESLint boundary rules, Material theme with light/dark tokens, Transloco;
`core/` (auth service with in-memory session, interceptor chain, guards, typed API clients, models);
`shared/` primitives; `layout/` shell with language switcher and user menu; features: login, users list
(table, search, sort, paging, role filter, all states), user form, profile, change password, audit; RTL and
responsive behaviour.

**Verify:** `npm run build` and `npm run lint` clean; every screen exercised against the running API as each
of the three roles; loading, empty and error states demonstrated (empty state via an impossible search,
error state by stopping the API); Arabic switch flips direction with no layout breakage; mobile width at
375px usable. Evidence: screenshots per screen per role per direction.

**Commits:**
```text
chore: scaffold angular workspace with material, eslint boundaries and i18n
feat: implement angular authentication, interceptor chain and route guards
feat: implement application shell with responsive navigation and language switcher
feat: implement user list with search, sorting, pagination and role filter
feat: implement user create and edit forms with reactive validation
feat: implement profile and password change screens
feat: implement audit log screen for administrators
feat: add arabic localization and RTL support
```

## Phase 8 — Testing

**Build:** the full inventory in [10-testing-plan.md](10-testing-plan.md) — unit, integration and frontend.

**Verify:** `dotnet test` and `npm test` fully green, counts and durations pasted into the report; coverage
reported (as information, not a target); every row of the edge-case table green.

**Commits:**
```text
test: add domain and application unit tests for business rules
test: add integration test fixture with testcontainers sql server
test: add api integration tests for auth, crud and the authorization matrix
test: add audit, localization and problem-details contract tests
test: add angular tests for services, guards, interceptors and components
test: add playwright smoke suite for the five critical flows
```

## Phase 9 — Documentation and packaging

**Build:** README with all 17 required sections; Swagger polish (XML comments, examples, bearer scheme,
`ProducesResponseType`); Postman collection and environment with folders Authentication/Users/Profile/Roles/Audit
and a login test script that captures the token into `{{token}}`; `database/001_schema.sql` generated from
migrations, `002_seed.sql`, `003_sample_queries.sql`; `Dockerfile` and `docker-compose.yml`; `docs/14-demo-script.md`.

**Verify:** a clean-clone walk-through following only the README: `docker compose up` reaches a working
login; the Postman collection runs end to end; `001_schema.sql` applies to an empty database and matches the
migration; Swagger `Authorize` works against a real token.

**Commits:**
```text
docs: add api documentation, swagger examples and postman collection
feat: add sql scripts for schema, seed data and sample queries
chore: add docker configuration for api and sql server
docs: add readme, architecture documentation and demo script
```

## Phase 10 — Final review

1. Re-read the assignment section by section against [01-requirements-checklist.md](01-requirements-checklist.md);
   flip each row to `Verified` only with evidence, and leave anything unproven as `Built` with a note.
2. Execute the 41-item final quality gate; record the result of each item.
3. Run the pre-release security checklist in [08-security-plan.md](08-security-plan.md).
4. Act as a strict reviewer over the whole diff — architecture violations, duplication, naming, async misuse,
   missing validation or authorization, wrong status codes, Angular anti-patterns, a11y, RTL — and **fix**
   what is found rather than listing it.
5. Confirm no secret is present anywhere in history.

**Commits:**
```text
fix: address findings from the final security and code review
docs: complete requirements traceability matrix and security review
```

## Verification discipline

Every phase report states: current phase, files changed, decisions taken, verification output (build result,
tests executed, scenarios exercised), and remaining work. A requirement is never marked done from inspection
alone — the assignment's rule, and the reason the checklist starts life entirely `Planned`.

## Estimated sequencing

| Phase | Relative effort |
|---|---|
| 2 Backend foundation | 1.0 |
| 3 Authentication | 1.0 |
| 4 User management | 1.5 |
| 5 Audit | 0.5 |
| 6 Errors + localization | 0.5 |
| 7 Angular | 2.5 |
| 8 Testing | 1.5 |
| 9 Documentation | 1.0 |
| 10 Final review | 0.5 |

Phases 2-6 are backend-only and can be verified without any frontend. Phase 7 depends on the API contract
being frozen at the end of Phase 6; if the contract must change afterwards, the change is made in
[06-api-contract.md](06-api-contract.md) first, then in code, so the document never lags the implementation.
