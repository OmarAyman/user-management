# User Management Module

A full-stack user management module: **ASP.NET Core (.NET 10)** Clean Architecture API, **Angular 21** admin
SPA, **SQL Server 2022**, JWT authentication with rotating refresh tokens, role-based authorization, soft
delete, an append-only audit trail, and English/Arabic localization with right-to-left support.

| | |
|---|---|
| **Backend tests** | 94 unit, 145 integration (real SQL Server via Testcontainers) |
| **Frontend tests** | 55 unit/component (Vitest), 14 browser smoke (Playwright) |
| **Build** | zero warnings, warnings-as-errors on |

---

## Contents

- [Project overview](#project-overview)
- [Architecture](#architecture)
- [Technology stack](#technology-stack)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Database setup](#database-setup)
- [Configuration](#configuration)
- [Running the backend](#running-the-backend)
- [Running the frontend](#running-the-frontend)
- [Running the tests](#running-the-tests)
- [API documentation](#api-documentation)
- [Demo credentials](#demo-credentials)
- [Authorization matrix](#authorization-matrix)
- [Localization](#localization)
- [Docker](#docker)
- [Postman](#postman)
- [Project structure](#project-structure)
- [Design decisions](#design-decisions)
- [Security considerations](#security-considerations)
- [Known limitations](#known-limitations)

---

## Project overview

Administrators manage user accounts; every authenticated user manages their own profile. The module covers
authentication, authorization, user CRUD, search, paging, sorting, role filtering, soft delete with restore, a
full audit trail, structured errors, and two languages.

What the implementation is actually trying to demonstrate:

- **Security decisions with reasons.** The access token never touches web storage, refresh tokens rotate and
  detect reuse, sign-in cannot be used to discover whether an account exists, and the two attacks the brief
  names — IDOR and mass assignment — are prevented by the shape of the routes and models rather than by
  checks somebody has to remember.
- **Rules that cannot be bypassed.** Authorization is server-side, auditing is a property of persistence
  rather than of the caller, and the soft-delete filter is on by default with a single, tested opt-out.
- **Verification over assertion.** Nothing in this README is claimed without a test or a recorded run behind
  it, and the defects found along the way are documented rather than quietly fixed.

## Architecture

Clean Architecture, four projects, dependencies pointing inward:

```text
UserManagement.Api            HTTP, auth wiring, middleware, DI root, ProblemDetails, Swagger, Serilog
        |
        v
UserManagement.Application    use cases, DTOs, ports (interfaces), business rules
        |
        v
UserManagement.Domain         entities, enums, invariants, error codes   (no dependencies at all)
        ^
        |
UserManagement.Infrastructure EF Core, SQL Server, hashing, JWT, audit interceptors
```

| Project | May reference | Enforced by |
|---|---|---|
| `Domain` | nothing | architecture test |
| `Application` | `Domain` only — no EF Core, no ASP.NET Core | architecture test |
| `Infrastructure` | `Application`, `Domain` | project references |
| `Api` | all three, but touches `Infrastructure` only in its composition root | architecture test |

Three tests assert this, so a violation fails the build rather than a review. Full detail in
[docs/02-architecture.md](docs/02-architecture.md).

Use cases are one folder each — command/query, handler, DTO — injected directly into controllers. No
mediator: validation runs as an action filter and auditing as an EF interceptor, which are the two concerns a
pipeline would otherwise exist for ([ADR-0003](docs/12-decision-log.md)).

## Technology stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10 LTS, C# 14, nullable enabled, warnings as errors |
| API | ASP.NET Core controllers, RFC 7807 ProblemDetails, rate limiting, request localization |
| Persistence | EF Core 10, SQL Server 2022, migrations, global query filters, save-changes interceptors |
| Security | `PasswordHasher<T>` (PBKDF2-HMAC-SHA512), JWT bearer, rotating opaque refresh tokens |
| Validation | FluentValidation at the transport boundary |
| Logging | Serilog — console plus a rolling JSON file |
| Frontend | Angular 21 standalone + zoneless, Angular Material 21, Transloco, typed Reactive Forms |
| Tests | xUnit, NSubstitute, Testcontainers, Vitest, Angular testing utilities, Playwright |

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | **10.0** | `dotnet --version` |
| Node.js | **24 LTS** | Pinned by `frontend/.nvmrc`. Angular 21 supports `^20.19 \|\| ^22.12 \|\| ^24`; odd-numbered releases such as 25 are outside it |
| SQL Server | 2019+ | Local instance, LocalDB, or the Docker Compose service |
| Docker | any recent | Only for Compose and for the integration tests' database |

Nothing else is required. If you would rather not install the SDK or a database, use [Docker](#docker).

## Installation

```bash
git clone <repository-url>
cd user-management

dotnet restore
npm ci --prefix frontend
```

## Database setup

Three ways, in order of convenience.

**1. Let the API do it (default in Development).** `Database:MigrateOnStartup` is `true` in
`appsettings.Development.json`, so running the API creates the schema and seeds the demo accounts.

**2. EF Core migrations by hand.**

```bash
dotnet ef database update -p src/UserManagement.Infrastructure -s src/UserManagement.Api
```

**3. SQL scripts.**

```bash
sqlcmd -S localhost -E -Q "CREATE DATABASE UserManagement"
sqlcmd -S localhost -E -d UserManagement -i database/001_schema.sql
sqlcmd -S localhost -E -d UserManagement -i database/002_seed.sql
```

`001_schema.sql` is **generated from the migrations** by `database/generate-schema-script.ps1`, so the script
and the model cannot drift. It is idempotent, and it carries a `SET QUOTED_IDENTIFIER ON` header for a
concrete reason: SQL Server refuses to create a filtered index — or to insert into a table that has one —
without it, and `sqlcmd` does not set it by default. `002_seed.sql` contains PBKDF2 hash literals produced by
the application's own hasher; **no plaintext password appears in any script**.

`003_sample_queries.sql` documents the twelve queries the application issues and names the index each one
uses.

## Configuration

Secrets are never committed. `appsettings.json` ships empty, `appsettings.example.json` documents the shape
with placeholders, and `appsettings.Development.json` carries only a local Windows-auth connection string and
the demo passwords — no signing key.

```bash
# Development: optional. With no key configured the API generates an ephemeral one at startup and says so,
# which means a fresh clone runs with nothing to set up. Set one to keep sessions across restarts:
dotnet user-secrets set "Jwt:Key" "<48 or more random characters>" --project src/UserManagement.Api

# Production: required. The API refuses to boot on a missing, placeholder or too-short key.
export Jwt__Key="<48 or more random characters>"
export ConnectionStrings__DefaultConnection="Server=...;Database=UserManagement;..."
```

| Section | Purpose |
|---|---|
| `Jwt` | Issuer, audience, signing key, access-token lifetime (15 minutes) |
| `RefreshToken` | Cookie name, path, `Secure` flag, lifetime (7 days) |
| `Lockout` | Failed attempts before lockout (5) and its duration (15 minutes) |
| `PasswordPolicy` | Length and composition requirements (12–128, upper + lower + digit) |
| `RateLimiting` | Auth requests permitted per window, per client address |
| `Cors` | Explicit SPA origins — credentials are required by the refresh cookie, so `*` is not an option |
| `Seed` | Whether to create demo accounts, and their passwords |

## Running the backend

```bash
dotnet run --project src/UserManagement.Api --urls http://localhost:5080
```

- API: `http://localhost:5080`
- Swagger UI: `http://localhost:5080/swagger`
- OpenAPI document: `http://localhost:5080/openapi/v1.json`
- Health: `http://localhost:5080/health`

Port 5080 is what `frontend/proxy.conf.json` forwards to; changing one means changing the other.

## Running the frontend

```bash
cd frontend
nvm use            # or otherwise ensure Node 24
npm start
```

The SPA runs on `http://localhost:4200` and proxies `/api` to the backend, so the browser sees one origin —
which is what lets the httpOnly refresh cookie work in development without CORS entanglement.

## Running the tests

```bash
# Backend: 94 unit + 145 integration
dotnet test

# Fast loop, no Docker required
dotnet test tests/UserManagement.UnitTests

# Frontend: 55 unit and component tests
npm test --prefix frontend

# Browser smoke suite: needs the API and the SPA running
npm run e2e --prefix frontend
```

Integration tests run against **real SQL Server 2022 in a container** (Testcontainers). That is deliberate:
global query filters, filtered unique indexes, `rowversion` concurrency, collation-driven case-insensitivity
and `LIKE` translation are exactly what these tests exist to prove, and the in-memory provider models none of
them faithfully. On a machine without Docker, point them at any SQL Server instead:

```bash
export USERMANAGEMENT_TEST_SQL="Server=localhost;Database=UserManagementTests;Trusted_Connection=True;TrustServerCertificate=True"
```

The fixture asserts which database it connected to. That check exists because a configuration mistake once let
the suite run green against the wrong database — a test pointed somewhere unintended is worse than a failing
one.

## API documentation

Swagger UI is enabled in Development, with the JWT bearer scheme wired up so `Authorize` works end to end.
Every endpoint declares its response types, including error shapes.

| Method | Route | Access | Notes |
|---|---|---|---|
| POST | `/api/auth/login` | anonymous | Sets the rotating refresh cookie |
| POST | `/api/auth/refresh` | refresh cookie | Rotates the cookie, returns a new access token |
| POST | `/api/auth/logout` | authenticated | Revokes the presented refresh token; idempotent |
| GET | `/api/users` | any role | Paged, searchable, sortable, role-filterable. Active users only |
| GET | `/api/users/deleted` | **Admin** | The only read path over soft-deleted users |
| GET | `/api/users/availability` | authenticated | Boolean check for async form validation |
| GET | `/api/users/me` | authenticated | No id in the route — the subject comes from the token |
| PUT | `/api/users/me` | authenticated | First name, last name, email. No role field exists |
| POST | `/api/users/me/change-password` | authenticated | Requires the current password |
| GET | `/api/users/{id}` | any role | Admins also see deleted users |
| POST | `/api/users` | **Admin** | `201` + `Location` |
| PUT | `/api/users/{id}` | **Admin** | Includes role. Requires the concurrency token |
| DELETE | `/api/users/{id}` | **Admin** | Soft delete |
| POST | `/api/users/{id}/restore` | **Admin** | Requires the concurrency token |
| GET | `/api/roles` | authenticated | Read-only reference data |
| GET | `/api/audit-logs` | **Admin** | Paged, filterable, newest first |

Every error is `application/problem+json` with a **stable, never-translated `errorCode`** alongside a
localized `title` and `detail`, plus a `traceId` that also appears in the logs. The full catalogue of 18 codes
is in [docs/06-api-contract.md](docs/06-api-contract.md).

## Demo credentials

**Development only.** Created by the seeder in Development, or by `database/002_seed.sql`. They must never be
used in a deployed environment.

| Username | Password | Role |
|---|---|---|
| `admin` | `Admin@123456` | Admin |
| `jdoe` | `User@1234567` | User |
| `readonly` | `ReadOnly@1234` | ReadOnlyUser |

Development also seeds 24 additional users, so search, sorting, paging and the role filter have something to
work with.

## Authorization matrix

Enforced server-side. The table below is also a **test** — a `[Theory]` calls every mutating endpoint as all
three roles, so a capability cannot widen quietly.

| Capability | Admin | User | ReadOnlyUser |
|---|:---:|:---:|:---:|
| Login | Yes | Yes | Yes |
| View / search / filter / sort users | Yes | Yes | Yes |
| Create user | Yes | No | No |
| Edit any user | Yes | No | No |
| Delete user | Yes | No | No |
| Restore deleted user | Yes | No | No |
| View deleted users | Yes | No | No |
| View audit trail | Yes | No | No |
| Change another user's role | Yes | No | No |
| **Update own profile** | Yes | Yes | Yes |
| **Change own role** | No | No | No |

Two rows a reviewer usually asks about:

- **ReadOnlyUser can edit its own profile.** "Read-only" refers to other people's data; the brief's matrix
  grants self-profile editing to all three roles.
- **Nobody changes their own role, administrators included.** The profile model has no role field, and an
  administrator editing themselves through the admin route gets `422 CANNOT_CHANGE_OWN_ROLE`.

The frontend hides controls a role cannot use, which is courtesy rather than security — the browser smoke
suite calls the API directly with a read-only token and asserts `403` on each refused operation.

## Localization

English and Arabic, on both sides, with right-to-left support.

- **Backend:** `.resx` resources resolved per request from `?culture=` or `Accept-Language`. Validation
  messages, business errors and authentication failures are all localized. A parity test fails the build if a
  key is missing from either file, or if an Arabic value contains no Arabic characters — a missing translation
  otherwise falls back to English silently.
- **Frontend:** Transloco with runtime JSON catalogues, so the language switches without a reload. Direction
  is set once on `<html>`; Angular Material and the CDK read it from there, so overlays, sort arrows and the
  paginator mirror without any component knowing about it.
- **Styling:** logical properties only (`margin-inline-start`, `text-align: start`) — no `left`/`right` in
  application CSS. Emails, usernames and IP addresses are wrapped in `<bdi>` so bidirectional reordering
  cannot mangle them.
- **Contract:** error codes are never translated. The SPA maps them to its own catalogue, with a mandatory
  `errors.unknown` fallback so a code added server-side never renders as an empty dialog.

## Docker

For a reviewer who would rather not install the .NET SDK or SQL Server:

```bash
cp .env.example .env      # then set MSSQL_SA_PASSWORD and JWT_KEY
docker compose up --build
```

This starts SQL Server 2022 and the API on `http://localhost:5080`, applies migrations and seeds the demo
accounts. The API waits for the database's health check, so migrations do not race the engine's startup on a
cold volume. The container runs as a non-root user and has no default signing key — the startup guard refuses
to boot in Production without one, which is the behaviour you want from a carelessly configured deployment.

The SPA is still run with `npm start`: someone evaluating an Angular application wants the dev server.

## Postman

```text
postman/UserManagement.postman_collection.json
postman/UserManagement.postman_environment.json
```

Import both, then run **Authentication → Login (Admin)** first: its test script captures the access token and,
later, the created user's id and concurrency token, so nothing has to be copied by hand. The refresh token
never appears in a response body — Postman keeps the httpOnly cookie in its own jar, which is why *Refresh
session* needs no input.

Beyond the happy paths, the collection includes the requests worth showing a reviewer: a wrong password, an
invalid sort field, a stale concurrency token, and the mass-assignment payload the brief names.

## Project structure

```text
user-management/
├── UserManagement.slnx
├── Directory.Build.props        nullable, warnings-as-errors, analyzers - in one place
├── Directory.Packages.props     central package versions
├── NuGet.config                 nuget.org only, so a clone is reproducible
├── docker-compose.yml           api + SQL Server
├── database/
│   ├── 001_schema.sql           GENERATED from the migrations
│   ├── 002_seed.sql             demo accounts, hash literals only
│   ├── 003_sample_queries.sql   the queries the app issues, with the index each uses
│   └── generate-schema-script.ps1
├── docs/                        the design set, ADRs, audit policy, demo script
├── postman/
├── src/
│   ├── UserManagement.Api/           controllers, contracts, validators, error handling, middleware
│   ├── UserManagement.Application/   features/<area>/<use case>/, ports, common models
│   ├── UserManagement.Domain/        entities, enums, constants, invariants
│   └── UserManagement.Infrastructure/persistence, interceptors, repositories, security, seeding
├── tests/
│   ├── UserManagement.UnitTests/         domain, handlers, auditing, architecture
│   └── UserManagement.IntegrationTests/  real SQL Server, real HTTP pipeline
└── frontend/
    ├── e2e/                     five Playwright smoke specs
    └── src/app/
        ├── core/                singletons: auth, guards, interceptors, api clients, models
        ├── shared/              stateless components, directives
        ├── layout/              the application shell
        └── features/            auth, users, profile, audit - all lazy-loaded
```

## Design decisions

Recorded as ADRs in [docs/12-decision-log.md](docs/12-decision-log.md) — 20 of them, each with the
alternatives and the cost. The ones that shaped the code most:

| Decision | Why |
|---|---|
| **CQRS folders, no MediatR** | Separation without a package, a pipeline and an indirection layer for thirteen use cases |
| **Access token in memory, refresh token in an httpOnly cookie** | Web storage makes any XSS a full session compromise; the cookie is what lets a reload survive anyway |
| **Refresh-token families** | Reuse detection can revoke one lineage instead of signing the user out of every device |
| **Uniqueness among *active* users only** | Audit identity is the immutable `UserId`, so permanently consuming a username to protect it was solving the wrong problem |
| **`rowversion` optimistic concurrency** | Two administrators editing one user is routine; without it the second save destroys the first and the audit trail calls it deliberate |
| **Auditing in an EF interceptor** | Makes auditing a property of persistence, so a future use case is audited whether its author remembered or not |
| **Validators at the transport boundary** | The action filter validates the request record; a validator written against the internal command would never have run |
| **Queries composed in the use case, executed through a port** | Keeps filter/sort/paging decisions next to the rule they serve without putting EF Core in the Application layer |
| **Real SQL Server in tests** | The in-memory provider models none of the behaviour these tests exist to prove |

## Security considerations

Full threat model, OWASP 2021 mapping and residual risks in
[docs/08-security-plan.md](docs/08-security-plan.md). In summary:

- **Passwords** — PBKDF2-HMAC-SHA512 via ASP.NET Core's `PasswordHasher`, per-user salt, rehash-on-verify. No
  hand-rolled cryptography. Plaintext exists only as a request field and is never assigned to an entity.
- **Sign-in discloses nothing** — an unknown username, a wrong password and a soft-deleted account return the
  same `401 INVALID_CREDENTIALS`, and verification always runs (against a dummy hash when the user does not
  exist) so timing does not separate them. `ACCOUNT_LOCKED` is returned only when the supplied password was
  *correct*, which tells a caller nothing they did not already know.
- **Brute force** — 5-failure lockout per account plus a per-IP rate limit on `/api/auth/*`. The two answer
  different questions and both are kept.
- **IDOR** — `/api/users/me` has no id segment, and `/api/users/{id}` is Admin-only. There is no code path
  where a client-supplied identifier selects the row being written.
- **Mass assignment** — request models carry only what may change, and `UnmappedMemberHandling.Disallow`
  rejects a payload with extra fields rather than silently dropping them.
- **SQL injection** — EF Core parameterisation throughout; sorting selects a branch from a whitelist, so
  client input never becomes part of a query string.
- **Soft-delete bypass** — a global query filter, with `IgnoreQueryFilters()` confined to one file with three
  justified callers and no client-controlled parameter.
- **Audit integrity** — append-only through the application: no update or delete exists on the repository,
  the entity, or the routing table. Password and token material is redacted at write time.
- **Secrets** — nothing real is committed; the API refuses to start in Production on a placeholder key.
- **Errors** — a `500` carries only a code and a trace id. No stack trace, exception type, SQL or
  configuration ever reaches a client.

## Known limitations

Stated rather than hidden.

1. **An access token stays valid for up to 15 minutes after sign-out, deletion or a role change.** Refresh is
   revoked immediately, so the session cannot be extended, but the current token lives out its lifetime. The
   alternative — a revocation lookup on every request — was rejected as a database hit per call.
2. **Search uses a leading wildcard**, which cannot seek. Covering indexes keep it cheap at this scale; the
   scaling path is a SQL Server full-text index with `CONTAINS`.
3. **No password reset by email.** A locked-out user waits 15 minutes or asks an administrator. Mail
   infrastructure would be an unverifiable dependency in an assessment.
4. **No multi-factor authentication.** Not requested; lockout and rate limiting cover the stated risks.
5. **Audit retention is not implemented.** Retention is an operator's policy decision and the schema supports
   it (`Timestamp` is indexed descending). Audit rows contain personal data — names, emails, IP addresses —
   and fall under whatever regime applies to the deployment.
6. **An operator with direct SQL access can still alter audit rows.** The mitigation is database permissions:
   the application's principal needs `INSERT` and `SELECT` on `AuditLogs` and nothing more.
7. **Restoring a deleted user can fail** if their username or email was taken while they were deleted. That is
   the deliberate consequence of releasing identifiers on deletion, and it returns a clear `409`.
8. **Browser coverage is five smoke specs, not an end-to-end suite.** Deep behaviour lives in the component
   and API tests, where it does not flake.
9. **Arabic copy is authored, not professionally reviewed.** A native-speaker pass would be the next step.
10. **The SPA is not containerised.** Only the API and the database are; someone reviewing an Angular
    application wants the dev server.
