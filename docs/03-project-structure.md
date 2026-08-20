# Project structure

## Repository root

```text
user-management/
├── UserManagement.slnx             .NET 10 solution format (the SDK that builds net10.0 understands it)
├── Directory.Build.props           shared compiler settings: nullable, warnings-as-errors, analyzers
├── Directory.Packages.props        central package version management
├── NuGet.config                    <clear /> + nuget.org only, so a clone does not depend on a private feed
├── .editorconfig                   formatting + analyzer severities
├── .gitattributes  .gitignore
├── README.md
├── docker-compose.yml              api + mssql (+ web)
├── database/
│   ├── 001_schema.sql              GENERATED from the migrations - never hand-edited
│   ├── 002_seed.sql                three demo users (precomputed PBKDF2 hashes only, no plaintext)
│   ├── 003_sample_queries.sql      the queries the app issues, each annotated with the index it uses
│   └── generate-schema-script.ps1  regenerates 001_schema.sql and prepends the required SET options
├── docs/                           this design set, plus the demo script
├── postman/
│   ├── UserManagement.postman_collection.json
│   └── UserManagement.postman_environment.json
├── src/
│   ├── UserManagement.Api/
│   ├── UserManagement.Application/
│   ├── UserManagement.Domain/
│   └── UserManagement.Infrastructure/
├── tests/
│   ├── UserManagement.UnitTests/
│   └── UserManagement.IntegrationTests/
└── frontend/                       Angular 21 workspace
```

**Why `frontend/` sits beside `src/` rather than inside it:** `src/` holds the .NET solution's projects and
is what `dotnet` globbing, `Directory.Build.props` and the solution file cover. Dropping an npm workspace
into that tree makes both toolchains noisier (restore, analyzers, file watchers) for no benefit. The
assignment's layer layout for `src/` is followed exactly.

## Backend

### `src/UserManagement.Domain` — no dependencies

```text
Common/
  BaseEntity.cs                     Id
  IAuditableEntity.cs               CreatedAt, CreatedBy, LastModifiedAt, LastModifiedBy
  ISoftDeletable.cs                 IsDeleted, DeletedAt, DeletedBy
Entities/
  User.cs                           behaviour-bearing: ChangeRole, SoftDelete, Restore,
                                    RecordFailedLogin, RecordSuccessfulLogin, SetPasswordHash
  Role.cs
  AuditLog.cs
  RefreshToken.cs
Enums/
  AuditAction.cs                    Insert, Update, Delete, Restore, RoleChange
  RevocationReason.cs               Rotated, ReuseDetected, Logout, PasswordChanged,
                                    RoleChanged, UserDeleted, Expired
Constants/
  RoleNames.cs                      Admin, User, ReadOnlyUser
  RoleIds.cs                        stable seed ids
  ErrorCodes.cs                     stable machine-readable codes shared with localization
  AuditRedaction.cs                 Redacted + NeverPersisted lists (13-audit-policy.md)
Exceptions/
  DomainException.cs
  DomainRuleViolationException.cs
```

### `src/UserManagement.Application` — depends on Domain only

```text
Common/
  Abstractions/
    ICommandHandler.cs  IQueryHandler.cs
    ICurrentUserService.cs  IClientInfoProvider.cs  IDateTimeProvider.cs
    IPasswordHasher.cs  IAccessTokenIssuer.cs  IRefreshTokenService.cs
    IUserRepository.cs  IRoleRepository.cs  IAuditLogRepository.cs  IUnitOfWork.cs
    IMessageLocalizer.cs
  Exceptions/
    NotFoundException.cs  ConflictException.cs  ValidationException.cs
    ForbiddenOperationException.cs  AuthenticationFailedException.cs
  Models/
    PagedResult.cs  PagedQuery.cs  SortDirection.cs
  Security/
    Policies.cs                     policy name constants
  Extensions/
    QueryableExtensions.cs          ApplyPaging, ApplySort (whitelist-driven)
Features/
  Auth/
    Login/            LoginCommand, Handler, Validator, LoginResponse
    RefreshToken/     RefreshTokenCommand, Handler
    Logout/           LogoutCommand, Handler
  Users/
    GetUsers/         GetUsersQuery, Handler, UserListItemDto, SortFieldMap
    GetDeletedUsers/  GetDeletedUsersQuery, Handler   (Admin-only; the only query-filter opt-out)
    GetUserById/      GetUserByIdQuery, Handler, UserDetailsDto
    CheckAvailability/ CheckAvailabilityQuery, Handler, AvailabilityDto
    CreateUser/       CreateUserCommand, Handler, Validator
    UpdateUser/       UpdateUserCommand, Handler, Validator
    DeleteUser/       DeleteUserCommand, Handler
    RestoreUser/      RestoreUserCommand, Handler
    GetMyProfile/     GetMyProfileQuery, Handler
    UpdateMyProfile/  UpdateMyProfileCommand, Handler, Validator
    ChangeMyPassword/ ChangeMyPasswordCommand, Handler, Validator
  Roles/
    GetRoles/         GetRolesQuery, Handler, RoleDto
  AuditLogs/
    GetAuditLogs/     GetAuditLogsQuery, Handler, AuditLogDto
DependencyInjection.cs              AddApplication(): handlers + validators
```

### `src/UserManagement.Infrastructure` — depends on Application + Domain

```text
Persistence/
  ApplicationDbContext.cs
  Configurations/    UserConfiguration, RoleConfiguration, AuditLogConfiguration,
                     RefreshTokenConfiguration
  Interceptors/      AuditableEntitySaveChangesInterceptor, AuditSaveChangesInterceptor
  Repositories/      UserRepository       QueryActive() / QueryIncludingDeleted() - the single
                                          IgnoreQueryFilters() site (ADR-0004)
                     RoleRepository, RefreshTokenRepository, AuditLogRepository (Phase 5)
                     IUnitOfWork is implemented by ApplicationDbContext itself: a wrapper class would
                     add a file and an indirection without adding a capability
  Migrations/        EF Core generated
  Seeding/           DbSeeder (roles + three demo users, idempotent)
Security/
  AspNetPasswordHasher.cs           wraps PasswordHasher<User>, plus the dummy-hash timing defence
  AccessTokenIssuer.cs              JWT creation (JsonWebTokenHandler)
  JwtClaimNames.cs                  sub, username, role, jti - issued and read unmapped
  OpaqueTokenGenerator.cs           256-bit random token + SHA-256 hex hashing
  RefreshTokenService.cs            families, rotation, reuse detection, revocation
Auditing/
  AuditPayloadBuilder.cs            EF-free: change list -> redacted JSON (unit-testable)
Configuration/
  JwtOptions.cs  RefreshTokenOptions.cs   bound + validated at startup
Localization/                       (Phase 6)
  ResxMessageLocalizer.cs
  Resources/Messages.resx, Messages.ar.resx
Time/
  SystemDateTimeProvider.cs
DependencyInjection.cs              AddInfrastructure(configuration)
```

### `src/UserManagement.Api` — composition root

```text
Program.cs                          minimal hosting; pipeline order documented in 02-architecture.md
Controllers/
  AuthController.cs                 login, refresh, logout
  UsersController.cs                CRUD + restore + me + change-password
  RolesController.cs
  AuditLogsController.cs
Contracts/                          HTTP-shaped request records (mapped to commands)
  Auth/LoginRequest.cs
  Users/CreateUserRequest.cs, UpdateUserRequest.cs, UpdateProfileRequest.cs,
        ChangePasswordRequest.cs, UserQueryParameters.cs
Middleware/
  CorrelationIdMiddleware.cs
ErrorHandling/
  ValidationExceptionHandler.cs  NotFoundExceptionHandler.cs  ConflictExceptionHandler.cs
  ForbiddenExceptionHandler.cs   AuthenticationExceptionHandler.cs
  ConcurrencyExceptionHandler.cs DbUpdateConcurrencyException -> 409 RESOURCE_MODIFIED
  GlobalExceptionHandler.cs      last in the chain: logs + generic 500
Services/
  CurrentUserService.cs  ClientInfoProvider.cs
Filters/
  ValidationFilter.cs               runs FluentValidation before the handler
Configuration/
  JwtKeyGuard.cs                    refuses a placeholder/missing signing key outside Development;
                                    generates an ephemeral one inside it, so nothing is ever committed
  CorsOptions.cs                    explicit SPA origins (credentials are required by the refresh cookie)
  PasswordPolicyOptions.cs  LockoutOptions.cs        (Phase 3-4)
  SwaggerConfiguration.cs  LocalizationConfiguration.cs  (Phase 6, 9)
appsettings.json
appsettings.example.json            placeholders only, safe to commit
Dockerfile
```

### `tests/`

```text
UserManagement.UnitTests/
  Domain/           UserTests, RoleChangeTests, SoftDeleteTests, LockoutTests
  Application/
    Auth/           LoginCommandHandlerTests, RefreshTokenHandlerTests
    Users/          CreateUserHandlerTests, UpdateUserHandlerTests, DeleteUserHandlerTests,
                    RestoreUserHandlerTests, UpdateMyProfileHandlerTests,
                    ChangeMyPasswordHandlerTests, GetUsersQueryHandlerTests
    Validators/     one class per validator
  Auditing/         AuditEntryBuilderTests (redaction, exclusion, RoleChange row)
  Architecture/     LayerDependencyTests, QueryFilterOptOutTests
UserManagement.IntegrationTests/
  TestSupport/      SqlServerFixture (real SQL Server via Testcontainers, schema built by the
                    migrations; USERMANAGEMENT_TEST_SQL overrides it for a machine without Docker),
                    TestCurrentUserService / TestClientInfoProvider, TestData
                    (named TestSupport, not Infrastructure, so it cannot be confused with the
                    UserManagement.Infrastructure namespace in a using directive)
  Persistence/      SchemaTests, SoftDeleteTests, UniquenessTests, ConcurrencyTests, AuditTrailTests
  Infrastructure/   ApiTestFixture (WebApplicationFactory), AuthenticatedClientFactory  (Phase 3+)
  Auth/             LoginEndpointTests, RefreshEndpointTests, TokenValidationTests
  Users/            UsersCrudTests, UsersListQueryTests, ProfileTests,
                    AuthorizationMatrixTests, SoftDeleteTests
  Audit/            AuditTrailTests, AuditAuthorizationTests
  Errors/           ProblemDetailsContractTests, LocalizedErrorTests
```

## Frontend — `frontend/`

```text
frontend/
├── .nvmrc                          "24" - Node 24 LTS is required, not suggested (ADR-0017)
├── angular.json  package.json  tsconfig*.json  eslint.config.js  vitest config
├── playwright.config.ts            Chromium only
├── e2e/                            five smoke specs (ADR-0015)
│   ├── admin-login.spec.ts         admin-creates-user.spec.ts
│   ├── user-list-query.spec.ts     readonly-cannot-mutate.spec.ts
│   └── arabic-rtl.spec.ts
├── public/
└── src/
    ├── main.ts                     bootstrapApplication + provideZonelessChangeDetection
    ├── styles/                     theme.scss (Material tokens), _rtl.scss, _tokens.scss
    ├── assets/i18n/en.json, ar.json
    └── app/
        ├── app.config.ts           providers: router, http + interceptor chain, Material, i18n
        ├── app.routes.ts           top-level routes with lazy loadChildren
        ├── core/
        │   ├── auth/               auth.service.ts (signals), session.model.ts,
        │   │                       token-refresh.coordinator.ts
        │   ├── guards/             auth.guard.ts, role.guard.ts
        │   ├── interceptors/       correlation-id, auth-token, api-error, auth-refresh, logging
        │   ├── http/               api-error.model.ts (typed union), api-client.ts
        │   ├── services/           users-api, roles-api, audit-api, locale.service,
        │   │                       notification.service, breakpoint.service
        │   └── models/             user.model.ts, role.model.ts, paged-result.model.ts,
        │                           audit-log.model.ts
        ├── shared/
        │   ├── components/         confirm-dialog, empty-state, error-state, loading-bar,
        │   │                       page-header, role-badge
        │   ├── directives/         has-role.directive.ts
        │   └── pipes/              localized-date.pipe.ts
        ├── layout/                 app-shell, header, language-switcher, user-menu, side-nav
        └── features/
            ├── auth/               login.page.ts + login.form
            ├── users/              users-list.page.ts (table, search, sort, paging, role filter),
            │                       user-form.page.ts (create/edit), users.routes.ts
            ├── profile/            profile.page.ts, change-password.page.ts
            └── audit/              audit-list.page.ts, audit.routes.ts
```

### ESLint boundary rules

```text
core/**      may not import shared/** or features/**
shared/**    may not import features/** or core/services/**
features/**  may not import another features/* sibling
layout/**    may import core/** and shared/** only
```

## Naming and style conventions

| Element | Convention |
|---|---|
| C# namespaces | file-scoped, matching folder path |
| C# DTOs, commands, queries | `record`, `sealed` where not inherited |
| C# classes | `sealed` by default; primary constructors for DI |
| JSON over the wire | camelCase, enforced by one `JsonSerializerOptions` |
| Angular files | kebab-case with a role suffix: `*.page.ts`, `*.service.ts`, `*.guard.ts`, `*.directive.ts` |
| Angular components | standalone, `OnPush`-equivalent under zoneless, explicit `imports` arrays (no shared barrel) |
| Translation keys | dotted domain paths: `users.list.columns.username` |
| Error codes | `SCREAMING_SNAKE`, shared between API `ProblemDetails.type` and the i18n catalogues |
