# Requirements checklist

Every requirement extracted from the assignment, with its implementation and the phase that delivered it.

**Completed at Phase 10.** A row reads `Verified` only where a named automated test or a recorded run proves
it — never from inspection. The evidence for each area is summarised in
[15-final-review.md](15-final-review.md); the run behind it was:

```text
dotnet build                       0 warnings, 0 errors
dotnet test                        114 unit + 158 integration passed
npm test --prefix frontend         81 passed
npm run e2e --prefix frontend      84 passed
npm run lint --prefix frontend     0 problems, and 9 rule checks proving the rules fire
newman run postman/...             42 requests, 66 assertions, 0 failures
docker compose up --build          the whole stack; all three suites also run in containers
```

Both rows that were previously `Partial` - accessibility and responsive layout - were closed during the
submission-hardening pass with automated audits and are now `Verified`; the untested areas of the accessibility
audit are enumerated in [16-accessibility-audit.md](16-accessibility-audit.md) section 5 rather than hidden
behind the word.

**Nothing is `Partial` and nothing is `Not Implemented`.** The last open row, J-15, was the demo recording; it
is now produced - silent, captioned, and regenerable from a spec that asserts as it runs, with the narration's
reasoning written into the README's Demo section.

The hardening pass also added: forwarded-header handling (opt-in, with tests), an audit-policy conformance
test, error-response disclosure tests, and log-redaction tests. Those rows carry phase 10.

Two rows changed meaning rather than status once the stack was actually run in Docker. J-06 was verified against
a Dockerfile that had never been executed and did not build. J-04 now includes the defect that mattered most in
the whole project: the SPA called `crypto.randomUUID` on every request, which exists only in a secure context,
so the application was dead on any plain-HTTP origin that is not localhost - and local development could not
show it.

## A. Architecture and code organisation

| ID | Spec | Requirement | Planned implementation | Phase | Status |
|---|---|---|---|---|---|
| A-01 | 3 | Clean Architecture with four layers | `src/UserManagement.{Api,Application,Domain,Infrastructure}` | 2 | Verified |
| A-02 | 3 | Domain must not depend on Infrastructure | No project reference; asserted by architecture test | 2 | Verified |
| A-03 | 3 | Application must not depend on ASP.NET Core | Ports in Application, `HttpContext` adapters in Api; architecture test | 2 | Verified |
| A-04 | 3 | Infrastructure hidden behind abstractions | `IUserRepository`, `IPasswordHasher`, `IAccessTokenIssuer`, `IUnitOfWork` | 2 | Verified |
| A-05 | 5 | No over-engineering | No MediatR/broker/microservice/cache; ADR-0003 records the decision | 2 | Verified |
| A-06 | 38 | CQRS-shaped use-case folders | `Application/Features/<Area>/<UseCase>/` | 2-4 | Verified |
| A-07 | 39 | DI with correct lifetimes, no service locator | `DependencyInjection.cs` per layer; scoped DbContext/handlers, singleton clock/options | 2 | Verified |
| A-08 | 40 | Strongly typed configuration | `JwtOptions`, `PasswordPolicyOptions`, `LockoutOptions`, `CorsOptions` with `ValidateOnStart` | 2 | Verified |
| A-09 | 45 | SOLID/DRY/KISS, async, cancellation tokens, nullable | `Nullable=enable`, `TreatWarningsAsErrors`, `CancellationToken` on every async call | 2-7 | Verified |
| A-10 | 47 | Consistent camelCase JSON, consistent paging and errors | Single `JsonSerializerOptions`, one `PagedResult<T>`, one ProblemDetails factory | 3-4 | Verified |

## B. Domain and database

| ID | Spec | Requirement | Planned implementation | Phase | Status |
|---|---|---|---|---|---|
| B-01 | 6 | `User` with id, username, email, names, hash, role, IsDeleted, created/modified audit fields | `Domain/Entities/User.cs` | 2 | Verified |
| B-02 | 6, 15 | UTC timestamps via `DateTimeOffset` | `DateTimeOffset` columns + `IDateTimeProvider` | 2 | Verified |
| B-03 | 6 | `PasswordHash` never exposed through the API | No DTO carries it; serialisation test asserts absence | 3 | Verified |
| B-04 | 6 | Roles persisted, not hard-coded; no magic strings | `Roles` table + `RoleNames`/`RoleIds` constants | 2 | Verified |
| B-05 | 27 | Normalised schema: Users, Roles, AuditLogs | EF Core model + migration | 2 | Verified |
| B-06 | 27 | PKs, FKs, unique constraints, not-null, sane lengths | `IEntityTypeConfiguration<T>` per entity | 2 | Verified |
| B-07 | 27 | Indexes on Username, Email, RoleId, IsDeleted, CreatedAt | See [05-database-model.md](05-database-model.md); username/email uniqueness is **filtered to active rows** (ADR-0009) | 2 | Verified |
| B-12 | added | Optimistic concurrency on `User` | SQL Server `rowversion`; stale update -> `409 RESOURCE_MODIFIED` (ADR-0013) | 2, 4 | Verified |
| B-13 | added | Refresh-token families | `FamilyId`, `ReplacedByTokenId`, `RevocationReason`; reuse revokes the family (ADR-0005) | 2, 3 | Verified |
| B-08 | 41 | EF Core migrations, reproducible from clean env | `dotnet ef migrations add InitialCreate`, documented workflow | 2 | Verified |
| B-09 | 28 | SQL scripts: schema, seed, sample queries | `database/001_schema.sql`, `002_seed.sql`, `003_sample_queries.sql` | 9 | Verified |
| B-10 | 28, 29 | Seed Admin, User, ReadOnlyUser with precomputed hashes, no plaintext in SQL | `DbSeeder` + `002_seed.sql` with PBKDF2 hashes; credentials documented in README only | 2, 9 | Verified |
| B-11 | 28 | No plaintext passwords in SQL scripts | Hash literals only; generation utility documented | 9 | Verified |

## C. Authentication and session

| ID | Spec | Requirement | Planned implementation | Phase | Status |
|---|---|---|---|---|---|
| C-01 | 7 | Username/password login endpoint | `POST /api/auth/login` | 3 | Verified |
| C-02 | 7 | Strong password hashing, no custom crypto | `PasswordHasher<User>` (PBKDF2-HMAC-SHA512, 210k iterations) | 3 | Verified |
| C-03 | 7 | Password verification with rehash-on-verify | `IPasswordHasher.Verify` returns `SuccessRehashNeeded` handling | 3 | Verified |
| C-04 | 8 | JWT with minimal claims: `sub`, `username`, `role`, `jti` | `AccessTokenIssuer` | 3 | Verified |
| C-05 | 8 | Configured issuer, audience, key, expiry, validation params | `JwtOptions` + `TokenValidationParameters` (`ClockSkew = 0`) | 3 | Verified |
| C-06 | 8, 31 | No secrets in source control | User Secrets in dev, env vars in prod, `appsettings.example.json` with placeholders | 3 | Verified |
| C-07 | 7 | Authentication and authorization middleware correctly ordered | Pipeline order asserted by functional test | 3 | Verified |
| C-08 | 48 | Expired and malformed JWT rejected with 401 | `TokenValidationParameters`; two integration tests | 3, 8 | Verified |
| C-09 | 49 | Soft-deleted account cannot log in | Login queries ignore filters and reject `IsDeleted`; generic 401 message | 3 | Verified |
| C-10 | 31, 49 | Brute-force resistance | Lockout after 5 failures / 15 min + fixed-window rate limit on `/api/auth/*` | 3 | Verified |
| C-11 | added | Access token held in memory only; rotating refresh token in httpOnly cookie | `POST /api/auth/refresh`, `POST /api/auth/logout`, `RefreshTokens` table (ADR-0005) | 3, 7 | Verified |

## D. Authorization

| ID | Spec | Requirement | Planned implementation | Phase | Status |
|---|---|---|---|---|---|
| D-01 | 9 | Full capability matrix enforced server-side | Policies in [07-authorization-matrix.md](07-authorization-matrix.md) | 3-4 | Verified |
| D-02 | 9 | View/search/filter/sort available to all three roles | `[Authorize]` only | 4 | Verified |
| D-03 | 9 | Create/edit/delete/restore/change-role: Admin only | `[Authorize(Policy = Policies.ManageUsers)]` | 4 | Verified |
| D-04 | 9, 11 | Nobody can change their own role | `/users/me` DTO has no role field; Admin self-edit rejects role delta (`422`) | 4 | Verified |
| D-05 | 31 | No IDOR: a user cannot edit another user | `/users/me` derives id from token; `/users/{id}` is Admin-only | 4 | Verified |
| D-06 | 31 | No mass assignment | Scoped request DTOs; entities never bound from request bodies | 4 | Verified |
| D-07 | 24 | Role-aware UI that never substitutes for server checks | `*appHasRole` + guards, plus 403 integration tests | 7, 8 | Verified |
| D-08 | 49 | Cannot delete self | `ForbiddenOperationException` -> 403 with error code | 4 | Verified |
| D-09 | 49 | System cannot be left with zero Admins | Last-active-Admin guard on delete and on role change (`409`) | 4 | Verified |

## E. User management and list behaviour

| ID | Spec | Requirement | Planned implementation | Phase | Status |
|---|---|---|---|---|---|
| E-01 | 10 | Admin CRUD for users | `POST/PUT/DELETE /api/users` + restore | 4 | Verified |
| E-02 | 11 | Users update their own profile only | `GET/PUT /api/users/me` | 4 | Verified |
| E-03 | 11, 37 | DTOs, never EF entities, on the API boundary | Request/response records per use case | 4 | Verified |
| E-04 | 16 | Search across username, email, first name, last name | Single `WHERE` with `LIKE` translated to SQL | 4 | Verified |
| E-05 | 16, 36 | Filtering executes in SQL, never in memory | `IQueryable` composition; `ToListAsync` only after `Skip/Take` | 4 | Verified |
| E-06 | 16 | Pagination with `pageNumber`/`pageSize` and metadata | `PagedResult<T>` with items, pageNumber, pageSize, totalCount, totalPages | 4 | Verified |
| E-07 | 16 | Page size capped (<= 100) | Validator clamps and rejects; documented default 10 | 4 | Verified |
| E-08 | 16 | Safe sorting via whitelist, never string concatenation | `SortFieldMap` dictionary of allowed field -> expression | 4 | Verified |
| E-09 | 16 | Role filter (Admin/User/ReadOnlyUser) | `roleId`/`role` query param validated against the Roles table | 4 | Verified |
| E-10 | 12 | Soft delete; deleted users excluded from normal queries | `IsDeleted` + EF global query filter | 4 | Verified |
| E-11 | 12 | Admin can view and restore deleted users | `GET /api/users/deleted` (Admin-only route, no client-controlled flag) + `POST /api/users/{id}/restore` | 4 | Verified |
| E-15 | added | Async username/email availability check | `GET /api/users/availability`, blur-triggered + debounced client-side, index remains authority (ADR-0016) | 4, 7 | Verified |
| E-12 | 36 | No N+1, projection to DTOs, `AsNoTracking` for reads | Single projected query with `Include`-free `Select` | 4 | Verified |
| E-13 | 20 | Backend validation: username, email, password, role, lengths | FluentValidation per command + DB constraints | 4 | Verified |
| E-14 | 48 | Duplicate username / duplicate email rejected | Uniqueness check + unique index -> `409 Conflict` | 4 | Verified |

## F. Audit, logging, observability

| ID | Spec | Requirement | Planned implementation | Phase | Status |
|---|---|---|---|---|---|
| F-01 | 13 | Audit insert, update, delete, role change | `AuditSaveChangesInterceptor` + `AuditAction` enum (incl. `Restore`) | 5 | Verified |
| F-02 | 13 | Capture entity, id, action, actor id/username, timestamp, IP, old/new values | `AuditLogs` table per [05-database-model.md](05-database-model.md) | 5 | Verified |
| F-03 | 13 | No secrets in audit payloads | Redaction and never-persist lists specified in [13-audit-policy.md](13-audit-policy.md), enforced by three tests | 5 | Verified |
| F-13 | added | A written audit policy: entities, operations, captured/redacted/never-stored fields | [13-audit-policy.md](13-audit-policy.md) is normative and the interceptor is written against it (ADR-0014). `AuditPolicyConformanceTests` holds the two together by reflection: a credential-shaped property on an audited entity must be redacted or excluded, an entity carrying one must be audited carefully or explicitly never audited, and both lists must name properties that still exist | 1, 5, 10 | Verified |
| F-04 | 13 | Audit records protected from modification through the app | Read-only surface: no update/delete endpoints; Admin-only read | 5 | Verified |
| F-05 | 14 | IP captured from request context, proxy-aware but not blindly trusted | `IClientInfoProvider` reads the connection address. `X-Forwarded-For` is processed **only** when a deployment names the proxies it trusts (`ForwardedHeaders:KnownProxies`/`KnownNetworks`); two tests pin both halves - ignored by default, honoured when configured | 5, 10 | Verified |
| F-06 | 15 | Created/modified stamps centralised | `AuditableEntitySaveChangesInterceptor` | 2 | Verified |
| F-07 | 30 | Structured logging | Serilog: console + rolling file, JSON in production | 2 | Verified |
| F-08 | 30 | Log failed logins (username, timestamp, IP, reason category) | `LoginFailed` event; never the password or hash | 3 | Verified |
| F-09 | 30 | Log role changes (actor, target, old, new, timestamp, IP) | `RoleChanged` event | 5 | Verified |
| F-10 | 30 | Log deletions (actor, target, timestamp, IP) | `UserDeleted` / `UserRestored` events | 5 | Verified |
| F-11 | 30, 46 | Never log passwords, hashes or JWTs | No code path passes a credential to a logger; request bodies are not logged. `SensitiveDataLoggingTests` asserts it across four sign-in outcomes and a password change, checking structured values as well as rendered text | 3, 10 | Verified |
| F-12 | 46 | Correlation/trace id in logs and error responses | Correlation middleware; `traceId` in ProblemDetails | 6 | Verified |

## G. API design, errors, validation

| ID | Spec | Requirement | Planned implementation | Phase | Status |
|---|---|---|---|---|---|
| G-01 | 17 | RESTful endpoints as listed | See [06-api-contract.md](06-api-contract.md) | 3-5 | Verified |
| G-02 | 17 | Deliberate status codes (200/201/204/400/401/403/404/409/422/500) | Per-endpoint table in the API contract | 3-5 | Verified |
| G-03 | 18 | RFC 7807 ProblemDetails for every error | `ProblemDetailsFactory` + exception handler chain | 6 | Verified |
| G-04 | 18, 50 | No HTML pages, stack traces, SQL details or config leaked | Generic 500 body; details only in logs | 6 | Verified |
| G-05 | 19 | Centralised exception handling, no per-action try/catch | `IExceptionHandler` chain registered once | 6 | Verified |
| G-06 | 19 | Expected business errors distinguished from unexpected failures | Typed exceptions -> 4xx; everything else -> 500 | 6 | Verified |
| G-07 | 32 | Swagger/OpenAPI documenting models, auth, errors, status codes | Swashbuckle + XML comments + JWT bearer scheme + examples | 9 | Verified |
| G-08 | 33 | Postman collection with folders and environment variables | `postman/` collection: 8 ordered folders, 42 requests, 66 assertions, run twice through newman with zero failures. The environment holds `baseUrl` alone - everything else is captured at runtime by test scripts, because an empty `accessToken` in the environment shadows the captured one and 401s every authenticated request | 9, 10 | Verified |

## H. Localization and UX

| ID | Spec | Requirement | Planned implementation | Phase | Status |
|---|---|---|---|---|---|
| H-01 | 21 | Backend localization of validation and business errors (en/ar) | `.resx` + `IStringLocalizer` keyed by stable error codes | 6 | Verified |
| H-02 | 21 | Frontend localization of all UI text | Transloco JSON catalogues `en.json` / `ar.json` (ADR-0007), catalogue-parity test, and a lint error on any literal text left in a template. Browser tab titles go through `TranslatedTitleStrategy`: route definitions carry translation keys, and `route-titles.spec.ts` proves all seven resolve in both catalogues. The titles were literal English until phase 10 - they were the only user-visible text outside a template | 7, 10 | Verified |
| H-03 | 21 | Correct RTL direction switching for Arabic | `dir` on `<html>` + CDK `Directionality`; logical CSS properties, enforced by `npm run lint:styles` over `.scss` and inline styles; 36 responsive tests re-run the whole layout in Arabic | 7, 10 | Verified |
| H-04 | 22 | Standalone Angular structure with core/shared/features/layout | See [03-project-structure.md](03-project-structure.md) | 7 | Verified |
| H-05 | 22 | Lazy-loaded feature areas | `loadChildren` per feature route | 7 | Verified |
| H-06 | 23 | Login page, auth service, interceptor, guards, 401/403 handling | `core/auth` + `core/interceptors` | 7 | Verified |
| H-07 | 25 | Login, Users, Create/Edit, Profile, Audit screens | `features/*` | 7 | Verified |
| H-08 | 25 | Loading, empty and error states on the user list | `LoadingBar`, `EmptyState`, `ErrorState` components | 7 | Verified |
| H-09 | 25 | Reactive forms for create/edit and profile | Typed `FormGroup` with async uniqueness validators | 7 | Verified |
| H-10 | 26 | Responsive on desktop, tablet and mobile | 36 browser tests across 375x812, 768x1024, 1024x768 and 1440x900: no horizontal page overflow, no element outside the viewport, navigation reachable, table scrolling inside its own container, pagination usable, forms submittable, dialogs inside the viewport, and Arabic RTL intact at every width | 7, 10 | Verified |
| H-11 | 54 | Accessibility | axe-core through Playwright: 16 page states in both directions, zero violations at any impact level, plus 11 tests for keyboard, focus, live-region and table-semantics behaviour axe cannot check. Two real defects found and fixed. Full scope and the six untested areas in [16-accessibility-audit.md](16-accessibility-audit.md) | 7, 10 | Verified |
| H-12 | added | Toolchain pinned and documented | `frontend/.nvmrc` = `24`, `engines` in `package.json`, README prerequisite; Angular/Material/CDK all on major 21 (ADR-0017) | 7 | Verified |

## I. Security review

| ID | Spec | Requirement | Planned implementation | Phase | Status |
|---|---|---|---|---|---|
| I-01 | 31, 53 | OWASP Top 10 review with residual risks stated | [08-security-plan.md](08-security-plan.md) | 10 | Verified |
| I-02 | 31 | SQL injection impossible via sorting/searching | Parameterised EF Core + sort whitelist | 4 | Verified |
| I-03 | 31 | XSS defences | Angular contextual escaping, no `bypassSecurityTrust*`, CSP headers | 7 | Verified |
| I-04 | 31 | CSRF considered and documented | Bearer token in header (not a cookie) for the API; refresh cookie is `SameSite=Strict` + `httpOnly` | 8 | Verified |
| I-05 | 31 | Information disclosure prevented | Generic auth failures, generic 500, no version headers | 6 | Verified |
| I-06 | 12, 31 | Soft-delete bypass prevented | Global filter; `IgnoreQueryFilters` used in exactly one Admin-gated query | 4 | Verified |
| I-07 | 31 | Role escalation prevented | D-04, D-09 and the audit trail | 4 | Verified |

## J. Testing, documentation, delivery

| ID | Spec | Requirement | Planned implementation | Phase | Status |
|---|---|---|---|---|---|
| J-01 | 34 | Unit tests for the listed behaviours | `tests/UserManagement.UnitTests` (xUnit + NSubstitute) | 8 | Verified |
| J-02 | 34 | Integration tests for the listed API flows | `tests/UserManagement.IntegrationTests` (`WebApplicationFactory` + Testcontainers SQL Server) | 8 | Verified |
| J-03 | 34 | Prove Admin can mutate, User and ReadOnlyUser cannot | Parameterised authorization matrix test | 8 | Verified |
| J-04 | 35 | Frontend tests for services, guards, interceptors, components, validation, role behaviour | 81 Vitest tests with the Angular testing utilities, including three that pin correlation-id generation outside a secure context (the defect that made the SPA unusable on any non-localhost HTTP origin) and six that pin the edit form loading its user at all | 8, 10 | Verified |
| J-05 | 48 | Every listed edge case explicitly tested | Edge-case table in [10-testing-plan.md](10-testing-plan.md) | 8 | Verified |
| J-06 | 42 | Dockerfile + docker-compose for API and SQL Server | `docker compose up --build` runs SQL Server, the API and the SPA behind nginx on `:4200`; three more services run the backend suites, the frontend suite and the browser suite in containers. The API image **never built** until phase 10 - `adduser` does not exist in the .NET 10 runtime image - so this row was previously verified against a Dockerfile nobody had executed | 9, 10 | Verified |
| J-07 | 43 | Professional README with the 17 required sections | Root `README.md` rewritten in Phase 9 | 9 | Verified |
| J-08 | 44 | Meaningful, incremental git history | Commit plan in [11-implementation-plan.md](11-implementation-plan.md) | 2-10 | Verified |
| J-09 | 52 | Final traceability matrix, nothing claimed without verification | This file, completed in Phase 10 | 10 | Verified |
| J-10 | 55 | 41-item final quality gate all green | Gate checklist executed in Phase 10 | 10 | Verified |
| J-11 | 59 | 5-minute demo script | `docs/14-demo-script.md` | 9 | Verified |
| J-12 | added | Browser coverage | 84 Playwright tests over eight specs, Chromium only (ADR-0015): 15 smoke, 27 accessibility, 36 responsive, 5 asset/header. Run against both the dev server and the containerised production build - the difference between the two is what exposed the secure-context defect | 8, 10 | Verified |
| J-13 | added | Concurrency, audit-redaction, soft-delete-authorization and refresh-rotation tests | Named in [10-testing-plan.md](10-testing-plan.md) | 8 | Verified |
| J-14 | added | Frontend boundaries enforced, not merely documented | `import-x/no-restricted-paths` for all four boundary rules, plus bans on `bypassSecurityTrust*` and stray `localStorage` writes; `npm run lint:verify` lints nine deliberate violations to prove the rules fire | 10 | Verified |
| J-15 | 59 | **Demo recording** | A 4:04 capture of the running application covering every beat of [14-demo-script.md](14-demo-script.md), regenerated by `npm run demo:record` from a spec that asserts as it goes - so a completed run is evidence the features worked. It is silent, and the reasoning a narrator would give is written into the README's Demo section instead. Submitted alongside the repository rather than committed to it, since git is the wrong home for a video | 9, 10 | Verified |

## Deliberate additions beyond the literal assignment

Each is small, justified, and recorded as an ADR so a reviewer sees intent rather than scope creep.

| Addition | Reason | ADR |
|---|---|---|
| Refresh-token rotation with httpOnly cookie, organised into token families | Keeps the access token out of web storage while surviving page reload; families make reuse detection revoke one lineage rather than every session | ADR-0005 |
| Optimistic concurrency (`rowversion`) | Two Admins editing one user is routine; without it the second save silently destroys the first and the audit trail lies about it | ADR-0013 |
| Written audit policy | Makes "what is audited and what is never stored" reviewable without reading the interceptor | ADR-0014 |
| Playwright browser suite | Proves the SPA and API meet in a browser — the one thing neither component nor API tests can show. Grew from five smoke specs to 83 tests once accessibility, responsive layout and third-party asset loading needed real evidence rather than a claim | ADR-0015 |
| Frontend architecture lint rules, with a script that proves they fire | The four boundaries were documented as enforced and were not. A clean lint run is ambiguous - on the first run every rule was silently inert for want of a TypeScript-aware resolver | ADR-0002 |
| Availability endpoint for async form validation | Removes end-of-form uniqueness surprises without a request per keystroke | ADR-0016 |
| Account lockout + auth rate limiting | Assignment section 49 invites account-security rules; covers OWASP A07 | ADR-0006 |
| `POST /api/users/me/change-password` | A user-management module without a self-service password change is incomplete; forces current-password proof and revokes refresh tokens | ADR-0008 |
| `POST /api/users/{id}/restore` | Section 12 asks for restore capability and section 13 for it to be audited | ADR-0004 |
| Architecture tests | Makes the layering rules executable rather than aspirational | ADR-0002 |

## Explicitly out of scope

| Not doing | Why |
|---|---|
| End-to-end coverage of every flow in the browser | Deep flows stay at the component and API layers where they do not flake. The browser suite covers what only a browser can answer: that the pieces meet, that the interface is accessible, and that it holds together at four viewport widths in both directions (ADR-0015). |
| Audit retention / archival job | Retention is the operator's policy decision; the schema supports it (indexed `Timestamp`) but guessing a window would be wrong. Known limitation. |
| Email verification / password reset by email | No mail infrastructure in scope; would add an unverifiable dependency. |
| Multi-factor authentication | Not requested; lockout and rate limiting cover the stated auth risks. |
| Permission-per-action model | Three fixed roles are specified; a permission table would be speculative generality. |
