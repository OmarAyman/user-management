# User Management Module

A full-stack user management module: **ASP.NET Core (.NET 10)** Clean Architecture API, **Angular 21** admin
SPA, **SQL Server 2022**, JWT authentication with rotating refresh tokens, role-based authorization, soft
delete, an append-only audit trail, and English/Arabic localization with right-to-left support.

| | |
|---|---|
| **Backend tests** | 114 unit, 153 integration (real SQL Server via Testcontainers) |
| **Frontend tests** | 75 unit/component (Vitest), 83 browser tests (Playwright: 15 smoke, 27 accessibility, 36 responsive, 5 assets/headers) |
| **Build** | zero warnings, warnings-as-errors on; `eslint .` clean, with the boundary rules proven to fire |

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
  names - IDOR and mass assignment - are prevented by the shape of the routes and models rather than by
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
| `Application` | `Domain` only - no EF Core, no ASP.NET Core | architecture test |
| `Infrastructure` | `Application`, `Domain` | project references |
| `Api` | all three, but touches `Infrastructure` only in its composition root | architecture test |

Three tests assert this, so a violation fails the build rather than a review. Full detail in
[docs/02-architecture.md](docs/02-architecture.md).

Use cases are one folder each - command/query, handler, DTO - injected directly into controllers. No
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
| Logging | Serilog - console plus a rolling JSON file |
| Frontend | Angular 21 standalone + zoneless, Angular Material 21, Transloco, typed Reactive Forms, self-hosted Roboto and Material icon fonts |
| Tests | xUnit, NSubstitute, Testcontainers, Vitest, Angular testing utilities, Playwright |

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | **10.0** | `dotnet --version` |
| Node.js | **24 LTS** | Pinned by `frontend/.nvmrc` and by `engines`. Angular 21 supports `^20.19 \|\| ^22.12 \|\| ^24`, so npm warns `EBADENGINE` on an odd-numbered release such as 25. Everything still builds, lints and tests there - `src/test-setup.ts` exists so the specs do not depend on which Node version supplies the browser storage globals - but 24 is the supported version |
| SQL Server | 2019+ | Local instance, LocalDB, or the Docker Compose service |
| Docker | any recent | Enough on its own: [Compose](#docker) runs the application and all three test suites, so the SDK, Node and SQL Server above are only needed to work on the code |

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
concrete reason: SQL Server refuses to create a filtered index - or to insert into a table that has one - 
without it, and `sqlcmd` does not set it by default. `002_seed.sql` contains PBKDF2 hash literals produced by
the application's own hasher; **no plaintext password appears in any script**.

`003_sample_queries.sql` documents the twelve queries the application issues and names the index each one
uses.

## Configuration

Secrets are never committed. `appsettings.json` ships empty, `appsettings.example.json` documents the shape
with placeholders, and `appsettings.Development.json` carries only a local Windows-auth connection string and
the demo passwords - no signing key.

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
| `PasswordPolicy` | Length and composition requirements (12-128, upper + lower + digit) |
| `RateLimiting` | Auth requests permitted per window, per client address |
| `Cors` | Explicit SPA origins - credentials are required by the refresh cookie, so `*` is not an option |
| `ForwardedHeaders` | Proxies whose `X-Forwarded-For` may be believed. Absent by default, and absent means the header is ignored |
| `Seed` | Whether to create demo accounts, and their passwords |

### Running behind a reverse proxy

The audit trail records a client IP, and a lockout counts attempts per account, so a request's apparent origin
matters. `X-Forwarded-For` is therefore **ignored unless a deployment names the proxies it trusts** - an
unauthenticated caller can put anything in that header, and believing it would let an attacker forge the IP in
every audit row.

```bash
export ForwardedHeaders__KnownProxies__0="10.0.0.4"      # the load balancer's address
export ForwardedHeaders__KnownNetworks__0="10.0.0.0/24"  # or a CIDR range
export ForwardedHeaders__ForwardLimit="1"                # hops to walk back, default 1
```

With nothing configured the API uses the immediate connection address and says so at startup. Two integration
tests pin both halves: the header is ignored when no proxy is trusted, and honoured when one is.

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

The SPA runs on `http://localhost:4200` and proxies `/api` to the backend, so the browser sees one origin - 
which is what lets the httpOnly refresh cookie work in development without CORS entanglement.

## Running the tests

```bash
# Backend: 114 unit + 153 integration
dotnet test

# Fast loop, no Docker required
dotnet test tests/UserManagement.UnitTests

# Frontend: 75 unit and component tests
npm test --prefix frontend

# Browser suite, 83 tests: needs the API and the SPA running
npm run e2e --prefix frontend

# Lint, and a check that the architectural lint rules actually fire
npm run lint --prefix frontend
npm run lint:verify --prefix frontend
npm run lint:styles --prefix frontend
```

Integration tests run against **real SQL Server 2022 in a container** (Testcontainers). That is deliberate:
global query filters, filtered unique indexes, `rowversion` concurrency, collation-driven case-insensitivity
and `LIKE` translation are exactly what these tests exist to prove, and the in-memory provider models none of
them faithfully. On a machine without Docker, point them at any SQL Server instead:

```bash
export USERMANAGEMENT_TEST_SQL="Server=localhost;Database=UserManagementTests;Trusted_Connection=True;TrustServerCertificate=True"
```

The fixture asserts which database it connected to. That check exists because a configuration mistake once let
the suite run green against the wrong database - a test pointed somewhere unintended is worse than a failing
one.

**Two notes on the frontend test runner**, both reliability fixes rather than preferences:

- `frontend/vitest.config.ts` caps the worker pool. Vitest sizes it from the CPU count, and with the dev server
  or a .NET test host already running there is not always enough headroom - the run then fails with
  `Failed to start forks worker` for every file, which reads like catastrophic breakage and is a resource limit.
- `frontend/src/test-setup.ts` guarantees working `localStorage` and `sessionStorage` in specs. The project pins
  **Node 24** (`.nvmrc`), where the storage globals come from jsdom. From Node 25 the runtime provides its own
  Web Storage on the shared global, and without `--localstorage-file` it is a stub whose methods are all
  `undefined` - so twelve storage-touching specs fail with `localStorage.clear is not a function`, pointing at
  the specs rather than at the runtime. The setup file uses the environment's storage when it works and
  substitutes a real in-memory `Storage` when it does not, so the suite passes on 24 and on 25 alike. Both
  branches are tested (`src/test-setup.spec.ts`).

Every suite also runs in a container, which needs no SDK, no Node and no local SQL Server - see
[Docker](#docker).

**The browser suite needs both servers up**, and Chromium installed once:

```bash
npx playwright install chromium          # first time only
# then, with the API on :5080 and the SPA on :4200
npm run e2e --prefix frontend
```

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
| GET | `/api/users/me` | authenticated | No id in the route - the subject comes from the token |
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

Enforced server-side. The table below is also a **test** - a `[Theory]` calls every mutating endpoint as all
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

The frontend hides controls a role cannot use, which is courtesy rather than security - the browser smoke
suite calls the API directly with a read-only token and asserts `403` on each refused operation.

## Localization

English and Arabic, on both sides, with right-to-left support.

- **Backend:** `.resx` resources resolved per request from `?culture=` or `Accept-Language`. Validation
  messages, business errors and authentication failures are all localized. A parity test fails the build if a
  key is missing from either file, or if an Arabic value contains no Arabic characters - a missing translation
  otherwise falls back to English silently.
- **Frontend:** Transloco with runtime JSON catalogues, so the language switches without a reload. Direction
  is set once on `<html>`; Angular Material and the CDK read it from there, so overlays, sort arrows and the
  paginator mirror without any component knowing about it.
- **Styling:** logical properties only (`margin-inline-start`, `text-align: start`) - no `left`/`right` in
  application CSS. Emails, usernames and IP addresses are wrapped in `<bdi>` so bidirectional reordering
  cannot mangle them.
- **Contract:** error codes are never translated. The SPA maps them to its own catalogue, with a mandatory
  `errors.unknown` fallback so a code added server-side never renders as an empty dialog.

## Docker

Everything runs in containers: the application, both backend suites, the frontend suite and the browser suite.
Docker is the only prerequisite - no .NET SDK, no Node, no local SQL Server.

```bash
cp .env.example .env      # then set MSSQL_SA_PASSWORD and JWT_KEY
docker compose up --build
```

That starts three services and leaves the whole application on **http://localhost:4200**:

| Service | What it is |
|---|---|
| `mssql` | SQL Server 2022. The API waits for its health check, so migrations cannot race the engine on a cold volume |
| `api` | The API on `:5080`, applying migrations and seeding the demo accounts on first start |
| `web` | The production Angular bundle behind nginx on `:4200`, with `/api` proxied to the API |

The SPA and the API share one origin through the nginx proxy, which is what lets the httpOnly refresh cookie
work with no CORS negotiation and no `SameSite` exemption - the same arrangement the dev-server proxy gives
locally. Sign in at `http://localhost:4200` with the [demo credentials](#demo-credentials).

The API container runs as the base image's non-root `app` user and has no default signing key: the startup
guard refuses to boot in Production without one, which is the behaviour you want from a carelessly configured
deployment.

nginx serves the document with a real `Content-Security-Policy` (`script-src 'self'`, `frame-ancestors 'none'`,
no third-party origin), plus `X-Content-Type-Options`, `X-Frame-Options` and `Referrer-Policy`. Until phase 10
the only CSP in the project was the API's, which covers JSON - the one response that cannot execute script.
The policy needs no font exemption because **Roboto and the Material icon font are bundled**: on a network that
cannot reach `fonts.gstatic.com`, every icon used to render as its own ligature text, so the toolbar read
"visibility", "delete", "edit". The page now loads nothing from a third-party origin, and a test fails if that
changes.

### The suites, in containers

```bash
docker compose --profile test run --rm --build backend-tests     # 114 unit + 153 integration
docker compose --profile test run --rm --build frontend-tests    # lint, rule checks, 75 Vitest tests
docker compose --profile e2e  run --rm --build e2e               # 83 Playwright tests
```

Nothing in the `test` or `e2e` profile starts during `docker compose up`.

`--build` is not decoration. Compose builds a missing image but will not rebuild a stale one, so without it a
run after any code change tests the previous commit - which is how a frontend run here reported 72 tests
while the working tree had 75.

`backend-tests` brings up a **second** SQL Server (`mssql-test`) and points the integration fixture at it
through `USERMANAGEMENT_TEST_SQL`, so a test run cannot touch the data you are clicking through, and
Testcontainers never needs the Docker socket. `e2e` runs the Playwright image against the `web` container -
the stack must already be up.

Tear down with `docker compose down`, or `docker compose --profile test down -v` to discard both databases.
Stopping and starting again is safe: the API retries its startup migration, because a SQL Server container
answers its health check before it has finished bringing user databases online, and the first version of this
setup died on exactly that race the second time it was started.

### Behind a TLS-inspecting proxy

If your network runs one (Zscaler, Netskope and similar), image builds fail on `dotnet restore` or `npm ci`
with *the remote certificate is invalid because of errors in the certificate chain*. The host works because
the corporate root CA is in its trust store; a container has no such thing.

Drop the CA as a PEM `.crt` into `certs/` and rebuild - every stage that reaches the network trusts that folder
before it installs anything. `certs/.gitkeep` carries the one-liner for exporting it on Windows. The folder's
contents are gitignored: a CA belongs to a network, not to this repository.

## Postman

```text
postman/UserManagement.postman_collection.json
postman/UserManagement.postman_environment.json
```

Import both, then run **Authentication -> Login (Admin)** first: its test script captures the access token and,
later, the created user's id and concurrency token, so nothing has to be copied by hand. The refresh token
never appears in a response body - Postman keeps the httpOnly cookie in its own jar, which is why *Refresh
session* needs no input.

Beyond the happy paths, the collection includes the requests worth showing a reviewer: a wrong password, an
invalid sort field, a stale concurrency token, and the mass-assignment payload the brief names.

## Project structure

Plain ASCII connectors on purpose: this block was silently mangled twice by an editor encoding round-trip, and
box-drawing characters are what got mangled.

```text
user-management/
|-- UserManagement.slnx
|-- Directory.Build.props            nullable, warnings-as-errors, analyzers - in one place
|-- Directory.Packages.props         central package versions
|-- NuGet.config                     nuget.org only, so a clone is reproducible
|-- docker-compose.yml               mssql + api + web, plus test and e2e profiles
|-- .env.example                     copy to .env; sa password and JWT key are required
|-- certs/                           optional corporate root CAs for image builds (contents gitignored)
|-- database/
|   |-- 001_schema.sql               GENERATED from the migrations - never hand-edited
|   |-- 002_seed.sql                 demo accounts, hash literals only, no plaintext
|   |-- 003_sample_queries.sql       the queries the app issues, with the index each uses
|   `-- generate-schema-script.ps1   regenerates 001_schema.sql with the required SET options
|-- docs/                            the design set, ADRs, audit policy, demo script, final review
|-- postman/                         collection + environment: 42 requests, 66 assertions
|-- src/
|   |-- UserManagement.Api/          controllers, contracts, validation, errors, middleware
|   |-- UserManagement.Application/  Features/<area>/<use case>/, ports, common models
|   |-- UserManagement.Domain/       entities, enums, constants, invariants
|   `-- UserManagement.Infrastructure/  persistence, interceptors, repositories, security, seeding
|-- tests/
|   |-- Dockerfile                   runs both backend suites in a container
|   |-- UserManagement.UnitTests/    domain, handlers, auditing, architecture, configuration
|   `-- UserManagement.IntegrationTests/  real SQL Server, real HTTP pipeline
`-- frontend/
    |-- Dockerfile  nginx.conf       production bundle behind nginx, /api proxied same-origin
    |-- eslint.config.js             boundary rules, sanitizer ban, template a11y and i18n
    |-- scripts/                     prove the lint rules fire; no physical left/right in styles
    |-- e2e/                         83 Playwright tests: smoke, accessibility, responsive, assets
    |-- demo/                        records the walkthrough in docs/14-demo-script.md as video
    `-- src/app/
        |-- core/                    auth, guards, interceptors, i18n, api clients, models
        |-- shared/                  stateless components and directives
        |-- layout/                  the application shell
        `-- features/                auth, users, profile, audit, errors - all lazy-loaded
```

## Design decisions

Recorded as ADRs in [docs/12-decision-log.md](docs/12-decision-log.md) - 20 of them, each with the
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

- **Passwords** - PBKDF2-HMAC-SHA512 via ASP.NET Core's `PasswordHasher`, per-user salt, rehash-on-verify. No
  hand-rolled cryptography. Plaintext exists only as a request field and is never assigned to an entity.
- **Sign-in discloses nothing** - an unknown username, a wrong password and a soft-deleted account return the
  same `401 INVALID_CREDENTIALS`, and verification always runs (against a dummy hash when the user does not
  exist) so timing does not separate them. `ACCOUNT_LOCKED` is returned only when the supplied password was
  *correct*, which tells a caller nothing they did not already know.
- **Brute force** - 5-failure lockout per account plus a per-IP rate limit on `/api/auth/*`. The two answer
  different questions and both are kept.
- **IDOR** - `/api/users/me` has no id segment, and `/api/users/{id}` is Admin-only. There is no code path
  where a client-supplied identifier selects the row being written.
- **Mass assignment** - request models carry only what may change, and `UnmappedMemberHandling.Disallow`
  rejects a payload with extra fields rather than silently dropping them.
- **SQL injection** - EF Core parameterisation throughout; sorting selects a branch from a whitelist, so
  client input never becomes part of a query string.
- **Soft-delete bypass** - a global query filter, with `IgnoreQueryFilters()` confined to one file with three
  justified callers and no client-controlled parameter.
- **Audit integrity** - append-only through the application: no update or delete exists on the repository,
  the entity, or the routing table. Password and token material is redacted at write time.
- **Secrets** - nothing real is committed; the API refuses to start in Production on a placeholder key.
- **Logging** - no code path passes a password, hash or token to a logger, and request bodies are not logged.
  Five tests run sign-in and password-change through a capturing logger and assert that none of that material
  appears - including in structured values, which is where a credential leaks into a JSON sink without ever
  showing up in a console line.
- **Errors** - a `500` carries only a code and a trace id. No stack trace, exception type, SQL or
  configuration ever reaches a client.

## Known limitations

Stated rather than hidden.

1. **An access token stays valid for up to 15 minutes after sign-out, deletion or a role change.** Refresh is
   revoked immediately, so the session cannot be extended, but the current token lives out its lifetime. The
   alternative - a revocation lookup on every request - was rejected as a database hit per call.
2. **Search uses a leading wildcard**, which cannot seek. Covering indexes keep it cheap at this scale; the
   scaling path is a SQL Server full-text index with `CONTAINS`.
3. **No password reset by email.** A locked-out user waits 15 minutes or asks an administrator. Mail
   infrastructure would be an unverifiable dependency in an assessment.
4. **No multi-factor authentication.** Not requested; lockout and rate limiting cover the stated risks.
5. **Audit retention is not implemented.** Retention is an operator's policy decision and the schema supports
   it (`Timestamp` is indexed descending). Audit rows contain personal data - names, emails, IP addresses - 
   and fall under whatever regime applies to the deployment.
6. **An operator with direct SQL access can still alter audit rows.** The mitigation is database permissions:
   the application's principal needs `INSERT` and `SELECT` on `AuditLogs` and nothing more.
7. **Restoring a deleted user can fail** if their username or email was taken while they were deleted. That is
   the deliberate consequence of releasing identifiers on deletion, and it returns a clear `409`.
8. **Browser coverage is 83 tests, not an exhaustive end-to-end suite.** The weight is in accessibility and
   responsive layout - things only a browser can answer. Deep behaviour lives in the component and API tests,
   where it does not flake.
9. **Arabic copy is authored, not professionally reviewed.** A native-speaker pass would be the next step.
10. **Screen-reader narration and 400% browser zoom are not tested.** Every automated accessibility check
    passes and the untested areas are listed in [docs/16-accessibility-audit.md](docs/16-accessibility-audit.md)
    section 5, but "no axe violations" is not "accessible".
11. **The containerised stack serves plain http.** Real deployment terminates TLS in front of it, which is why
    the refresh cookie's `Secure` flag is configurable. Worth knowing because a non-HTTPS, non-localhost origin
    is not a *secure context* in the browser's sense - `crypto.subtle`, `navigator.clipboard` and
    `crypto.randomUUID` are unavailable there. Nothing here depends on the first two, and the third has a
    fallback since it took the whole application down once.
12. **No demo recording is included.** [docs/14-demo-script.md](docs/14-demo-script.md) is a shot-by-shot
    five-minute script and every screen it calls for works, but the video itself is not in this repository.


