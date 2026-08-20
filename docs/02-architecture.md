# Architecture

## 1. Goals and constraints

The module is a **modular monolith**: one API process, one SPA, one relational database. Every structural
decision below serves one of six ranked priorities, in this order:

> **Security -> Correctness -> Architecture -> Maintainability -> UX -> Documentation**

Explicitly rejected as unjustified at this scope: microservices, event sourcing, message brokers,
Kubernetes, distributed caching, a generic repository over every aggregate, and MediatR
(see [12-decision-log.md](12-decision-log.md)).

## 2. Layering

```text
                +---------------------------------+
                |   UserManagement.Api            |  HTTP, auth wiring, middleware, DI root,
                |   (ASP.NET Core)                |  ProblemDetails, Swagger, Serilog
                +----------------+----------------+
                                 | depends on
                +----------------v----------------+
                |   UserManagement.Application    |  use cases, DTOs, validators, ports
                |   (pure .NET, no ASP.NET Core)  |  (interfaces), authorization rules
                +----------------+----------------+
                                 | depends on
                +----------------v----------------+
                |   UserManagement.Domain         |  entities, enums, invariants,
                |   (no dependencies at all)      |  domain errors, business rules
                +---------------------------------+
                                 ^
                                 | implements Application ports, maps Domain
                +----------------+----------------+
                |   UserManagement.Infrastructure |  EF Core, SQL Server, password hasher,
                |                                 |  JWT issuer, audit writer, clock
                +---------------------------------+
```

### Dependency rules (enforced by project references)

| Project | May reference | Must never reference |
|---|---|---|
| `Domain` | nothing | anything |
| `Application` | `Domain` | `Infrastructure`, EF Core, `Microsoft.AspNetCore.*` |
| `Infrastructure` | `Application`, `Domain` | `Api` |
| `Api` | `Application`, `Infrastructure`, `Domain` | - |

`Api` touches `Infrastructure` **only** in its DI composition root; controllers depend on Application
abstractions. An architecture test (`Application_must_not_depend_on_AspNetCore`) asserts this so the rule
cannot rot silently.

### Why `Application` stays ASP.NET-free

`ICurrentUserService` and `IClientInfoProvider` are declared in `Application/Common/Abstractions` and
implemented in `Api` (they read `HttpContext`). Use cases therefore consume *authenticated identity* and
*client IP* as plain values. That is what makes handler unit tests trivial and keeps `HttpContext` out of
business logic.

## 3. Use-case organisation (CQRS-shaped, no mediator)

Each use case is one folder holding its command/query, handler, validator and use-case DTO:

```text
Application/Features/Users/CreateUser/
    CreateUserCommand.cs           record CreateUserCommand(...)
    CreateUserCommandHandler.cs    sealed class : ICommandHandler<CreateUserCommand, Guid>
    CreateUserCommandValidator.cs  FluentValidation
```

Handlers are injected directly into controllers through the marker interfaces
`ICommandHandler<TCommand, TResult>` and `IQueryHandler<TQuery, TResult>`. No dispatcher, no pipeline
behaviours, no reflection. Input validation runs as an ASP.NET Core action filter before the handler is
entered, so no handler re-validates request shape.

**Validators live in `Api/Validation`, not beside the commands** (ADR-0019). The filter validates the request
record MVC bound, so a validator written against the internal command would never be resolved — validation
that looks present and provides nothing. Field shape is therefore an API-layer concern and semantics stay in
the handlers, where no caller can bypass them.

**Queries are composed here and executed through a port.** `Where`/`OrderBy`/`Select`/`Skip`/`Take` are plain
`System.Linq` and belong next to the rule they serve; `ToListAsync` and `CountAsync` are EF Core, so they
travel through `IQueryExecutor` (ADR-0020). That is what lets the user listing push filtering, sorting and
paging into SQL without the Application project referencing the ORM.

**Rationale:** the assignment's own guidance is to avoid CQRS for buzzword compliance. Folder-per-use-case
delivers the separation and testability; MediatR would add a package, a pipeline and an indirection layer
for roughly ten use cases with no measurable gain.

## 4. Request pipeline (ordered)

```text
 1. Serilog request logging      structured request/response, enriched with correlation id
 2. Correlation-id middleware    X-Correlation-Id in, echoed out, pushed into the log scope
 3. Security headers             nosniff, DENY, no-referrer, CSP; Server header suppressed
 4. Request localization         ?culture= then Accept-Language -> en | ar
 5. Exception handler            two-handler chain -> localized ProblemDetails
 6. HTTPS redirection + HSTS     production only
 7. CORS                         named policy, explicit SPA origin, AllowCredentials for refresh cookie
 8. Authentication               JWT bearer: issuer, audience, lifetime, signature
 9. Authorization                policies from 07-authorization-matrix.md
10. Rate limiting                fixed window, applied to /api/auth/* only
11. Endpoints (controllers)      thin: no try/catch, no business logic
```

Order is behaviour, and the tests hold it in place. **Localization precedes the exception handler**, not just
the endpoints: the handlers resolve their titles, details and field messages from
`CultureInfo.CurrentUICulture`, so a failure raised anywhere downstream is rendered in the caller's language
only if the culture was already set. An Arabic error-response test fails if these two ever swap.

The exception handler wraps everything below it, so a failure in authentication, authorization, the rate
limiter or an endpoint becomes a ProblemDetails rather than a stack trace.

## 5. Cross-cutting concerns and where they live

| Concern | Abstraction (Application) | Implementation | Notes |
|---|---|---|---|
| Current identity | `ICurrentUserService` | `Api/Services/CurrentUserService` | Reads `sub`, `role`, `jti` from `ClaimsPrincipal`; never trusts identity from a request body |
| Client IP / user agent | `IClientInfoProvider` | `Api/Services/ClientInfoProvider` | `HttpContext.Connection.RemoteIpAddress`; forwarded headers honoured only with configured known proxies |
| Time | `IDateTimeProvider` | `Infrastructure/Time/SystemDateTimeProvider` | `DateTimeOffset.UtcNow`; makes lockout and expiry deterministic in tests |
| Password hashing | `IPasswordHasher` | `Infrastructure/Security/AspNetPasswordHasher` | Wraps `PasswordHasher<User>` (PBKDF2-HMAC-SHA512, ASP.NET v3 format), supports rehash-on-verify |
| Token issuing | `IAccessTokenIssuer`, `IRefreshTokenService` | `Infrastructure/Security/*` | JWT signing plus rotating opaque refresh tokens |
| Auditing | `IAuditService` | `AuditSaveChangesInterceptor` | Change-tracker diff with a redaction list |
| Created/modified stamps | - | `AuditableEntitySaveChangesInterceptor` | One place; handlers never set timestamps |
| Localized messages | `IMessageLocalizer` | `Infrastructure/Localization/ResxMessageLocalizer` | Resolves stable error codes to en/ar text |
| Persistence | `IUserRepository`, `IRoleRepository`, `IAuditLogRepository`, `IRefreshTokenRepository`, `IUnitOfWork` | `Infrastructure/Persistence/*` | Purpose-built repositories exposing composable queries, not a generic `IRepository<T>`. `IUserRepository` is the only soft-delete opt-out point (section 7) |
| Concurrency | - | `User.RowVersion` + `ConcurrencyExceptionHandler` | SQL Server `rowversion`; stale writes become `409 RESOURCE_MODIFIED` instead of silent overwrites (ADR-0013) |

## 6. Auditing and stamping design

Two EF Core `SaveChangesInterceptor`s, registered once:

- **`AuditableEntitySaveChangesInterceptor`** sets `CreatedAt`/`CreatedBy` on `Added` and
  `LastModifiedAt`/`LastModifiedBy` on `Modified`, from `ICurrentUserService` + `IDateTimeProvider`.
- **`AuditSaveChangesInterceptor`** builds `AuditLog` rows from the change tracker *before*
  `SaveChanges` (to capture original values) and persists them *after* (to capture generated keys).
  What it captures, excludes, redacts and must never persist is specified normatively in
  [13-audit-policy.md](13-audit-policy.md) — the interceptor is written against that document, and the
  never-persist list is a shared constant referenced by both the interceptor and its tests, so the two
  cannot drift apart silently (ADR-0014).

  Audit identity is the target's immutable `UserId` (`EntityId`), with usernames stored only as snapshots
  (`EntityDisplayName`, `PerformedByUsername`). That is what allows a soft-deleted user's username to be
  reused without making history ambiguous (ADR-0009).

Role changes are detected as a property-level delta on `User.RoleId` and emitted as an extra
`AuditAction.RoleChange` row alongside the `Update` row, because role escalation is the single
highest-value event for a reviewer or an incident responder to find.

**Consequence:** auditing cannot be bypassed by a new use case that forgets to call an audit service.
It is a property of persistence, not of the caller.

## 7. Soft delete design

`User` implements `ISoftDeletable`. A global query filter
(`modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsDeleted)`) excludes deleted rows from every query
by default.

Opting out is **an authorization decision, not a parameter**. `UserRepository` is the only type that calls
`IgnoreQueryFilters()`, in a single private helper, and an architecture test scans the source tree to fail the
build if the call appears in any other file. Three repository methods build on that helper, each with a
justified consumer:

| Method | Consumer | Why it needs deleted rows |
|---|---|---|
| `QueryIncludingDeleted()` | `GetDeletedUsersQueryHandler` behind `Policies.ManageUsers` | the Admin-only deleted listing |
| `GetByIdIncludingDeletedAsync()` | Admin by-id lookup, restore, refresh-token owner resolution | an Admin must be able to load and restore a deleted user |
| `GetForAuthenticationAsync()` | `LoginCommandHandler` | sign-in must see a deleted row to refuse it, rather than report "no such user" |

`GET /api/users` has no soft-delete parameter at all, so there is nothing for a non-Admin to set (ADR-0004).

Deletion sets `IsDeleted`, `DeletedAt` and `DeletedBy`. There is no hard-delete path on the API surface.
`DELETE` against an already-deleted user returns `409 Conflict` rather than `404`, so an Admin can tell
"already gone" from "never existed".

## 8. Error handling

Controllers contain no `try/catch`. Business failures are typed exceptions raised in Application
(`NotFoundException`, `ConflictException`, `ForbiddenOperationException`, `ValidationException`,
`AuthenticationFailedException`), plus EF Core's `DbUpdateConcurrencyException` mapped to
`409 RESOURCE_MODIFIED` (ADR-0013), and translated by an `IExceptionHandler` chain into RFC 7807
`ProblemDetails` carrying a stable `type` URI, a localized `title`/`detail`, an `errors` dictionary for
validation failures, and `traceId`. Unexpected exceptions log at `Error` with full detail and return a
generic `500` body that discloses nothing but the trace id. Full contract in
[06-api-contract.md](06-api-contract.md).

## 9. Frontend architecture

Angular 21 standalone components, zoneless change detection, signals for state, Angular Material + CDK
for UI and bidirectionality.

```text
src/app/
  core/           app-lifetime singletons, provided once at bootstrap
    auth/         AuthService (signal-based session), token refresh orchestration
    guards/       authGuard, roleGuard (functional CanActivateFn)
    interceptors/ correlationId -> acceptLanguage -> authToken -> authRefresh -> apiError
    i18n/         TranslocoHttpLoader, TranslatedTitleStrategy (localized browser tab titles)
    services/     UsersApi, AuditApi, LocaleService, NotificationService
    models/       DTO interfaces mirroring the API contract 1:1
  shared/         stateless and reusable: ConfirmDialog, state panels (empty/error/loading),
                  HasRoleDirective
  layout/         AppShell: header, language switcher, user menu, responsive nav
  features/       lazy-loaded routed areas: auth/, users/, profile/, audit/
```

**Dependency direction:** `features -> core + shared`, `layout -> core + shared`; `core` never imports
`shared` or `features`; nothing outside `features` imports `features`; no feature imports a sibling. Enforced by
`import-x/no-restricted-paths` (`npm run lint`), which resolves each import to a real path first, so a violation
fails CI rather than a review. `npm run lint:verify` lints one deliberate violation per rule to prove the rules
fire at all - the frontend counterpart to the backend's architecture tests, and not a redundant one: the rules
were silently inert on their first run for want of a TypeScript-aware resolver. The rule earned its place
immediately, refusing a spec in `core/i18n/` that imported `features/users`.

**Interceptor order is behaviour, and the two directions are opposite.** Requests pass through the array top
to bottom; responses come back bottom to top. The registered order is therefore:

```text
correlationId  ->  acceptLanguage  ->  authToken  ->  authRefresh  ->  apiError  ->  network
                                                          <- typed ApiError <- HttpErrorResponse
```

Request-side interceptors (correlation id, `Accept-Language`, bearer token) run outbound. `apiError` is
**last**, which puts it closest to the network, so on the way back it converts `HttpErrorResponse` into the
typed `ApiError` union before anything else sees it. `authRefresh` is listed *before* it precisely so that it
receives the mapped error and can branch on a discriminated union rather than re-reading status codes.

Getting that pair the wrong way round compiles and runs: the refresh interceptor then receives the raw error,
falls into its defensive branch, and attempts a token refresh after a genuinely wrong password. The original
implementation had exactly this defect, and a frontend test caught it — the reason the ordering is documented
here as a rule rather than a preference.

`authRefresh` also queues concurrent 401s behind a single refresh call. That is not an optimisation: refresh
tokens rotate, so a second simultaneous refresh replays an already-used token, which the server correctly
treats as theft and answers by revoking the whole family.

State by kind:

| State kind | Mechanism |
|---|---|
| Server data (user list, audit list) | signal-based loaders in feature services, keyed by query params |
| Route state (page, size, sort, search, role filter) | URL query params as the single source of truth, so lists are deep-linkable and back/forward works |
| Session (access token, current user, role) | `AuthService` signals, in memory only |
| Form state | typed Reactive Forms |
| Locale and direction | `LocaleService` signal + `Directionality` from `@angular/cdk/bidi` |

## 10. What the frontend is explicitly not trusted for

Role-based hiding (`*appHasRole`) and route guards exist for UX only. Every mutation is re-authorized
server-side, and the integration suite proves that `User` and `ReadOnlyUser` receive `403` on admin routes
even when the UI would never issue the call.
