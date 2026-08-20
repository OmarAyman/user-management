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

**Rationale:** the assignment's own guidance is to avoid CQRS for buzzword compliance. Folder-per-use-case
delivers the separation and testability; MediatR would add a package, a pipeline and an indirection layer
for roughly ten use cases with no measurable gain.

## 4. Request pipeline (ordered)

```text
 1. Serilog request logging      structured request/response, enriched with correlation id
 2. Correlation-id middleware    X-Correlation-Id in, echoed out, pushed into the log scope
 3. ForwardedHeaders (opt-in)    only when KnownProxies/KnownNetworks are configured
 4. Exception handler            IExceptionHandler chain -> localized ProblemDetails
 5. Request localization         Accept-Language / ?culture= -> en | ar
 6. HTTPS redirection + HSTS     production only
 7. CORS                         named policy, explicit SPA origin, AllowCredentials for refresh cookie
 8. Authentication               JWT bearer: issuer, audience, lifetime, signature
 9. Authorization                policies from 07-authorization-matrix.md
10. Rate limiting                fixed window, applied to /api/auth/* only
11. Endpoints (controllers)      thin: no try/catch, no business logic
```

Order is behaviour, and a functional test asserts it: localization must precede endpoints so validation
messages resolve in the request culture, and the exception handler must wrap everything downstream.

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

Opting out is **an authorization decision, not a parameter**. `IUserRepository` exposes two
intention-named methods — `QueryActive()` and `QueryIncludingDeleted()` — and the second is the only place
in the codebase that calls `IgnoreQueryFilters()`, with an architecture test failing the build if the call
appears anywhere else. It has exactly two callers: `GetDeletedUsersQueryHandler`, reached only through
`GET /api/users/deleted` behind an Admin policy, and `LoginCommandHandler`, which must see a soft-deleted row
in order to refuse it rather than report "no such user". `GET /api/users` has no soft-delete parameter at
all, so there is nothing for a non-Admin to set (ADR-0004).

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
    interceptors/ correlationId -> authToken -> errorMapping -> authRefresh(401) -> logging
    services/     UsersApi, RolesApi, AuditApi, LocaleService
    models/       DTO interfaces mirroring the API contract 1:1
  shared/         stateless and reusable: ConfirmDialog, EmptyState, ErrorState, LoadingBar,
                  HasRoleDirective, LocalizedDatePipe
  layout/         AppShell: header, language switcher, user menu, responsive nav
  features/       lazy-loaded routed areas: auth/, users/, profile/, audit/
```

**Dependency direction:** `features -> core + shared`, `layout -> core + shared`; `core` never imports
`shared` or `features`; nothing outside `features` imports `features`. Enforced with
`no-restricted-imports` ESLint rules so a violation fails CI rather than a review.

**Interceptor order is behaviour.** Request-side interceptors (correlation id, bearer token) run outbound.
The error mapper converts `HttpErrorResponse` into a typed `ApiError` union in one place. The 401-refresh
interceptor sits downstream of the mapper so it branches on a typed discriminant instead of re-reading a
raw status code, and it queues concurrent 401s behind a single refresh call.

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
