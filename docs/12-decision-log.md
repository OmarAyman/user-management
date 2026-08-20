# Decision log

Each record states the decision, why it was taken over the alternatives, and what it costs. Decisions that
were *rejected* are recorded too — a reviewer learns more from what was considered and dropped than from a
list of choices presented as inevitable.

---

## ADR-0001 — Clean Architecture with four projects, .NET 10, Angular 21

**Status:** accepted

**Context:** the assignment mandates Clean Architecture with a specific dependency direction, .NET 8 or 10,
and Angular 21+.

**Decision:** four backend projects (`Api`, `Application`, `Domain`, `Infrastructure`) exactly as the
assignment lays them out, on **.NET 10 LTS** with **EF Core 10** and **SQL Server 2022**; frontend on
**Angular 21** with **Angular Material 21**. The Angular workspace lives in `frontend/` beside `src/` rather
than inside it, so the .NET solution glob, analyzers and file watchers do not have to ignore `node_modules`.

**Alternatives:** .NET 8 (also allowed, and the safer choice if a reviewer's SDK is older); a single project
with folders (faster, but the assignment explicitly grades layering); Angular 22 (newest, but the installed
CLI is 21 and 21 satisfies the stated floor).

**Consequences:** a reviewer needs the .NET 10 SDK; the Docker path removes that requirement entirely, which
is one reason Compose is provided rather than left optional.

---

## ADR-0002 — Layering rules are enforced by tests, not by convention

**Status:** accepted

**Context:** "Domain must not depend on Infrastructure" is a rule that holds until someone adds a convenient
`using`. Reviews catch it inconsistently.

**Decision:** three architecture tests in the unit-test project assert that `Domain` has no project
dependencies, that `Application` references no `Microsoft.AspNetCore.*` or EF Core assembly, and that
`Infrastructure` types are referenced from `Api` only in the composition root.

**Consequences:** an architectural violation fails the build rather than a review. Cost: one test class, and
the composition-root exception has to be expressed as an allow-list of one file.

---

## ADR-0003 — CQRS folder structure without MediatR

**Status:** accepted

**Context:** the assignment permits CQRS "if it improves separation" and explicitly warns against buzzword
compliance. The repository convention is to avoid introducing MediatR by default.

**Decision:** one folder per use case containing its command/query, handler, validator and DTO. Handlers
implement `ICommandHandler<T,R>` / `IQueryHandler<T,R>` and are injected straight into controllers. No
mediator, no pipeline behaviours. Validation runs as an action filter; auditing and stamping run as EF
interceptors — the two concerns a pipeline would otherwise be needed for.

**Alternatives:** MediatR (adds a dependency, a licence consideration and a layer of indirection for ~13 use
cases); services grouped by entity (`UserService` with twelve methods — the god-class the assignment warns
about).

**Consequences:** controllers list their handlers explicitly, which makes each endpoint's dependencies
obvious. Cross-cutting behaviour must be added as a filter or interceptor rather than a behaviour; both
exist and are the more idiomatic ASP.NET Core mechanism anyway.

---

## ADR-0004 — Soft delete via a global query filter, with an Admin-only restore

**Status:** accepted

**Context:** deleted users must be excluded from normal queries, and section 12 invites restore capability
if it fits the design.

**Decision:** `IsDeleted` plus an EF Core global query filter, so exclusion is the default rather than
something every query must remember. Restore is `POST /api/users/{id}/restore` and is audited as
`AuditAction.Restore`.

**Amended (Phase 1 review): `IgnoreQueryFilters()` is not reachable from a query parameter.** The original
design exposed `GET /api/users?includeDeleted=true`, which made soft-delete visibility a *value* travelling
through the controller rather than an *authorization decision*. A missing policy attribute would then have
turned into a data leak. The revised design:

- `IUserRepository` exposes intention-named methods: `QueryActive()` (filtered, the default for everything)
  plus three that see deleted rows. All four are built on a single private helper in `UserRepository` — the
  **only** place in the solution that calls `IgnoreQueryFilters()`, enforced by an architecture test that
  scans the source tree for the invocation (and ignores comments, so the rule can still be documented).
- The three opt-out methods each have a justified consumer: `QueryIncludingDeleted()` for the Admin-only
  `GetDeletedUsersQueryHandler` behind `Policies.ManageUsers`; `GetByIdIncludingDeletedAsync()` for Admin
  load-and-restore and for resolving a refresh token's owner; and `GetForAuthenticationAsync()` for sign-in,
  which must see a deleted row to refuse it (BR-05) rather than treat it as a missing user.
- Deleted users are read through a separate route, `GET /api/users/deleted`, carrying its own Admin policy.
  `GET /api/users` has no soft-delete parameter at all, so there is nothing for a non-Admin to set.

**Alternatives:** filtering in each query (one forgotten `Where` is a data leak); a database view (moves the
rule out of the code that is tested); hard delete with an archive table (loses referential history and
contradicts the assignment); keeping the boolean parameter with a policy check inside the handler (works,
but leaves the unsafe capability one forgotten `if` away).

**Consequences:** a query that legitimately needs deleted rows must opt out explicitly, visibly, and in a
place the tests already point at. See ADR-0009 for the uniqueness consequences of soft delete.

---

## ADR-0005 — Access token in memory, rotating refresh token in an httpOnly cookie

**Status:** accepted (user-approved addition beyond the literal assignment)

**Context:** the assignment requires JWT authentication but says nothing about storage. Putting a JWT in
`localStorage` or `sessionStorage` makes any XSS a full session compromise; keeping it only in memory logs
the user out on every page reload, which a reviewer will notice within the first minute of the demo.

**Decision:** a 15-minute access token held in an Angular signal (memory only, never in web storage), plus an
opaque 256-bit refresh token delivered as `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`, stored as a
SHA-256 hash, rotated on every use, with reuse of a rotated token revoking the whole chain. `/api/auth/refresh`
and `/api/auth/logout` support it, and the SPA refreshes silently on the first `401`.

**Amended (Phase 1 review): token families.** Rotation alone tells you a token was replaced; it does not tell
you which lineage a stolen token belonged to. Each login starts a **family** and every rotation inherits its
`FamilyId`, so the model is:

```text
RefreshToken(Id, TokenHash, UserId, FamilyId, ReplacedByTokenId,
             CreatedAt, CreatedByIp, ExpiresAt, RevokedAt, RevokedByIp, RevocationReason)
```

- The raw token exists only in the cookie and in the response pipeline — the database stores
  `SHA256(token)` as 64 hex characters, and the raw value is never logged.
- Normal rotation: the presented token gets `RevokedAt` + `RevocationReason = Rotated` and
  `ReplacedByTokenId` pointing at its successor, which carries the same `FamilyId`.
- **Reuse detection:** presenting a token that already has `ReplacedByTokenId` set means two clients hold
  tokens from one lineage, i.e. theft. The entire family is revoked with
  `RevocationReason = ReuseDetected` and the event is logged at `Warning`. Revoking the family — not every
  token the user owns — means a compromised session on one device does not sign the user out everywhere,
  while the compromised lineage dies immediately.
- `RevocationReason` is an enum (`Rotated`, `ReuseDetected`, `Logout`, `PasswordChanged`, `RoleChanged`,
  `UserDeleted`, `Expired`), which turns "why is this session gone?" from a support guess into a query.
- `ReplacedByTokenId` is a self-referencing FK to `RefreshTokens.Id` rather than a duplicated hash string,
  so the rotation chain is navigable and cannot drift out of sync with the row it names.

**Consequences:** one extra column set and a self-referencing FK, in exchange for per-lineage revocation and
an auditable rotation chain. Password change, role change and deletion revoke *all* families for the user,
because those events invalidate every session regardless of lineage.

**Alternatives:** `sessionStorage` (simplest, survives reload, but hands the token to any injected script);
in-memory with no refresh (secure, poor demo UX); refresh token in the response body (readable by script, so
it inherits the problem it was meant to solve).

**Consequences:** one extra table, two extra endpoints, and CORS must allow credentials for the SPA origin.
An already-issued access token stays valid for up to 15 minutes after logout or demotion; that residual
window is recorded as T-04 rather than solved with a per-request revocation lookup.

---

## ADR-0006 — Account lockout and auth rate limiting, with no disclosure

**Status:** accepted (addition, invited by section 49)

**Decision:** five consecutive failures lock an account for 15 minutes; `/api/auth/*` additionally carries a
fixed-window IP rate limit. The two controls answer different questions — lockout protects one account from
many guesses, the rate limit protects many accounts from one source — so both are kept.

**Amended (Phase 1 review): a deliberate, narrow use of `ACCOUNT_LOCKED`.** The review asked for both a
generic authentication failure that never reveals whether a username exists *and* an `ACCOUNT_LOCKED` error
code. Taken naively those conflict: telling an unauthenticated caller "this account is locked" confirms the
account exists. The resolution is to gate disclosure on **proof of knowledge of the password**:

| Outcome | Response |
|---|---|
| Unknown username | `401 INVALID_CREDENTIALS` |
| Known username, wrong password (locked or not) | `401 INVALID_CREDENTIALS` |
| Known username, **correct** password, account locked | `401 ACCOUNT_LOCKED` with the retry time |
| Known username, correct password, account soft-deleted | `401 INVALID_CREDENTIALS` |

A caller who supplies the correct password already knows the account exists, so `ACCOUNT_LOCKED` leaks
nothing to them — while an enumerating attacker without the password can never distinguish the four cases.
This is also the only version that is honest to the legitimate user: someone who mistyped five times and
then typed correctly is told to wait fifteen minutes instead of being told, misleadingly, that their
password is wrong.

Password verification always runs, including for unknown usernames (against a fixed dummy hash), so the
response time does not separate "no such user" from "wrong password".

**Consequences:** a user who genuinely forgot their password waits 15 minutes or asks an Admin, since no
email-based reset exists in scope — recorded as a known limitation. Lockout state is additionally visible to
Admins in the user list and in the logs. The four-way branch above is covered by four named tests.

---

## ADR-0007 — Transloco for runtime i18n; `dir` plus logical CSS for RTL

**Status:** accepted

**Context:** the UI must switch between English and Arabic interactively, including direction. Angular's
built-in `$localize` compiles one bundle per locale and switching means loading a different build.

**Decision:** Transloco with JSON catalogues loaded at runtime — the only i18n dependency added. Direction is
`dir`/`lang` on `<html>`, which Angular CDK's `Directionality` propagates to every Material overlay,
paginator and sort header. Application styles use logical properties (`margin-inline-start`, `inset-inline-*`,
`text-align: start`) exclusively, enforced by a stylelint rule. Arabic uses Latin digits (`ar-u-nu-latn`) so
IP addresses, ids and timestamps stay comparable with the logs and audit trail a reviewer reads side by side.

**Alternatives:** `$localize` (fails the interactive requirement); `ngx-translate` (equivalent, less actively
maintained); hand-rolled translation service (re-implements pluralisation and lazy catalogue loading badly).

**Consequences:** one runtime dependency and a second key catalogue to keep in sync, guarded by a parity test.
Translation keys must exist before a string is displayed, which is the intended pressure.

---

## ADR-0008 — Self-service password change is in scope

**Status:** accepted (addition)

**Context:** the assignment specifies profile update but never mentions changing one's own password. A user
management module without it is incomplete, and its absence would read as an oversight.

**Decision:** `POST /api/users/me/change-password`, requiring the current password, applying the same password
policy as creation, rotating the security stamp and revoking every refresh token for that user.

**Consequences:** one endpoint, one form, three tests. Password *reset* (forgotten password, out-of-band
delivery) stays out of scope because it needs mail infrastructure that cannot be demonstrated here.

---

## ADR-0009 — Username and email are unique among *active* users only

**Status:** accepted — **supersedes the original ADR-0009** ("uniqueness spans soft-deleted rows"), reversed
at the Phase 1 review.

**Context:** soft delete forces a choice about identifiers. The original decision used plain unique indexes
covering every row, so deleting `john` permanently consumed the name. The stated reason was audit clarity:
`AuditLog.PerformedByUsername` is denormalised, so two accounts having held `john` looked ambiguous.

**Why that reasoning was wrong.** It solved an *identity* problem with a *namespace* restriction. Audit
identity should never have depended on username uniqueness in the first place — a username is a mutable
label, a `UserId` is the identity. Making the name permanently unavailable also imposes a real operational
cost (an Admin cannot re-create a departed employee's account, and cannot free a mistyped username without
restoring a row they wanted gone) to protect an invariant that a correct audit schema does not need.

**Decision:**

1. **Filtered unique indexes** scoped to active rows:
   ```sql
   CREATE UNIQUE INDEX UQ_Users_ActiveUsername ON Users(Username) WHERE IsDeleted = 0;
   CREATE UNIQUE INDEX UQ_Users_ActiveEmail    ON Users(Email)    WHERE IsDeleted = 0;
   ```
   A soft-deleted user's username and email return to the pool immediately.
2. **`UserId` is the only identity.** It is immutable, never reused, and is what every audit row points at:
   `AuditLog.EntityId` holds the target `UserId`, and `AuditLog.PerformedByUserId` holds the actor's.
3. **Usernames in audit rows are snapshots, not keys.** `PerformedByUsername` and the new
   `EntityDisplayName` column record what the names *were* at the moment of the action, so the trail stays
   readable without a join and without implying uniqueness.
4. **Restore must re-check availability.** Restoring a deleted user whose username or email has since been
   taken by an active user fails with `409 RESOURCE_CONFLICT` (`USERNAME_ALREADY_EXISTS` /
   `EMAIL_ALREADY_EXISTS`) rather than violating the index at the database level. This is the new failure
   mode the reversal introduces, and it is covered by a named test.

**The ambiguity that motivated the old decision is now impossible:**

```text
UserId A123, Username "john"  -> soft-deleted.      Audit rows: EntityId = A123, EntityDisplayName = "john"
UserId B456, Username "john"  -> created later.     Audit rows: EntityId = B456, EntityDisplayName = "john"
```

Two accounts, one name, zero ambiguity: every row names the `UserId` it belongs to. Filtering the audit
list by user filters on `EntityId`, never on a name.

**Consequences:** filtered indexes are not usable for a `MERGE`-style upsert and are ignored by queries
whose predicate does not include `IsDeleted = 0` — irrelevant here, since the global query filter puts that
predicate on every normal query, which is precisely what makes the index usable for login lookups. Restore
gains a pre-flight availability check. Uniqueness checks in handlers must qualify with `IsDeleted = 0`, and
the database index remains the final authority under a race.

---

## ADR-0010 — Auditing and stamping live in EF Core interceptors

**Status:** accepted

**Context:** the assignment asks for centralised timestamp logic and a complete audit trail. Calling an audit
service from each handler works until one handler forgets.

**Decision:** two `SaveChangesInterceptor`s. One sets created/modified stamps; the other diffs the change
tracker, builds `AuditLog` rows before `SaveChanges` (for original values) and persists them after (for
generated keys), applying a redaction list.

**Alternatives:** explicit calls per handler (forgettable); SQL triggers (invisible to the application, hard
to test, cannot see the authenticated actor); temporal tables (excellent for history, but they capture rows
rather than *actions* and cannot record who or from which IP).

**Consequences:** auditing is a property of persistence, so a future use case is audited whether its author
thought about it or not. Cost: interceptors need the current user and clock, so they are registered as scoped
services rather than singletons, and audit behaviour is tested through the DbContext instead of in isolation.

---

## ADR-0011 — Stable machine-readable error codes alongside localized messages

**Status:** accepted

**Context:** error responses must be structured, localized in two languages, and consumable by both the SPA
and the tests. A localized message is a terrible branching key.

**Decision:** every `ProblemDetails` carries an `errorCode` (for example `LAST_ADMIN_CANNOT_BE_REMOVED`) that
is never translated, in addition to a localized `title` and `detail`. The SPA maps codes to its own catalogue;
tests assert on codes; a direct API consumer reading the response gets a localized sentence.

**Consequences:** every business error is declared in one place (`ErrorCodes`) and appears in three catalogues
(resx en, resx ar, frontend JSON). Parity tests keep them aligned, and adding an error is a deliberate act
rather than a stray string.

---

## ADR-0012 — Docker Compose is provided, and is the recommended reviewer path

**Status:** accepted

**Context:** the assignment makes Docker optional but requires it to work if included, and requires the app
to be reproducible from a clean environment.

**Decision:** ship a multi-stage `Dockerfile` for the API and a `docker-compose.yml` running API plus SQL
Server 2022, with migrations applied at container start in Development and the `sa` password supplied from a
gitignored `.env` (with `.env.example` committed). The frontend keeps `npm start` as its documented path,
since a reviewer evaluating an Angular app will want the dev server anyway.

**Consequences:** a reviewer without the .NET 10 SDK or a local SQL Server can still reach a working login.
Cost: the Compose file is a maintained artifact, and the Phase 9 verification step includes a clean
`docker compose up` from a fresh clone.

---

## ADR-0013 — Optimistic concurrency on `User` via a SQL Server `rowversion`

**Status:** accepted (Phase 1 review addition)

**Context:** two Admins editing the same user is not a hypothetical in an admin console — it is the normal
weekday. Without a concurrency token, the second save silently overwrites the first (a lost update), and the
audit trail records both changes as if they were intentional and sequential.

**Decision:** `User.RowVersion` mapped to a SQL Server `rowversion` column with `.IsRowVersion()`. SQL Server
maintains it; nothing in the application ever assigns it.

- The token is returned in `UserDetailsDto` as a base64 string and **required** on `PUT /api/users/{id}`,
  `PUT /api/users/me` and `POST /api/users/{id}/restore` — the operations that mutate an existing row a
  client has already read.
- EF Core adds the token to the `WHERE` clause of the `UPDATE`. Zero rows affected means someone else won,
  and EF throws `DbUpdateConcurrencyException`.
- A dedicated exception handler maps that to **`409 Conflict`** with `errorCode = RESOURCE_MODIFIED` and a
  localized message telling the user to reload. The response deliberately does **not** include the current
  server state: merging is a UI decision, and shipping the winning row inside a conflict body invites a
  blind retry that re-creates the lost update.
- `DELETE` does not require the token. Deletion is idempotent in intent, and BR-07 already rejects deleting
  an already-deleted row, so a stale delete cannot destroy an unseen edit.

**Alternatives:** a `LastModifiedAt` comparison (works, but depends on clock resolution and on nobody writing
the column directly — `rowversion` is maintained by the engine and cannot be spoofed); pessimistic locking
(holds locks across user think-time in a web app: unacceptable); last-write-wins (the status quo being
fixed).

**Consequences:** update DTOs carry an opaque token the client must round-trip, and the Angular forms must
keep it in the form model. Tests cover the race directly: two contexts read the same user, both update, the
second receives `409 RESOURCE_MODIFIED`.

---

## ADR-0014 — A written audit policy, not just an audit implementation

**Status:** accepted (Phase 1 review addition)

**Context:** the interceptor decides what gets audited by *behaviour*. A reviewer, an auditor and the next
developer all need to know what is captured and what is deliberately not, without reading the interceptor.

**Decision:** [13-audit-policy.md](13-audit-policy.md) is normative: it lists the audited entities, the
audited operations, the captured fields, the redacted fields, and the fields that must never be persisted
under any circumstance. The interceptor is written against that document, and three tests enforce the
"never persisted" list — including one that runs a password change end to end and asserts no audit row
contains password material in either `OldValues` or `NewValues`.

**Consequences:** adding an audited entity or field is a documentation change plus a test change, not a
silent behavioural drift. The redaction list lives in one constant, referenced by both the interceptor and
the tests, so the document and the code cannot disagree without a test failing.

---

## ADR-0015 — A five-scenario Playwright smoke suite, and nothing more

**Status:** accepted (Phase 1 review addition; reverses the original "no end-to-end tests" limitation)

**Context:** the original plan omitted browser tests to avoid flake and setup cost. The review asked for a
small smoke suite if it does not turn into a framework.

**Decision:** exactly five specs, run against the real API and a production build of the SPA:
admin login; admin creates a user; search + filter + sort + pagination on the user list; `ReadOnlyUser`
sees no mutation controls and is refused by the API; Arabic switch flips direction and translates the shell.

Constraints that keep it a smoke suite rather than a suite: one browser (Chromium), no visual snapshots, no
page-object framework beyond a thin login helper, data seeded through the API rather than the UI, and a hard
rule that a failing smoke test is fixed or deleted — never retried into green.

**Fallback, pre-committed:** if wiring Playwright against the API costs more than roughly an hour of setup
(container orchestration, HTTPS trust, cookie handling for the refresh flow), it is dropped and the decision
recorded here rather than left half-built. Backend integration tests plus component tests already cover the
same behaviour; the smoke suite exists to prove the pieces meet in a browser, which is genuinely something
neither layer can prove.

---

## ADR-0016 — Async uniqueness validation is UX only, over a boolean availability endpoint

**Status:** accepted (Phase 1 review addition)

**Context:** telling an Admin at the end of a form that the username is taken is poor UX; telling them on
every keystroke is a request per character.

**Decision:** `GET /api/users/availability?username=&email=` returns
`{ "usernameAvailable": bool, "emailAvailable": bool }` for authenticated callers, rate-limited, and checked
against **active** users only (ADR-0009). The Angular async validator runs on **blur**, not on keystroke,
and additionally applies `debounceTime(400)` and `distinctUntilChanged()` so a blur/focus cycle without an
edit issues nothing.

**Enumeration note:** every authenticated role may already list and search all users, so a boolean
availability check discloses nothing the user list does not. The endpoint is therefore authenticated rather
than Admin-only, which is what the profile email field needs. It is not anonymous, because that *would* be a
new enumeration surface.

**Correctness:** the validator never decides anything. The unique index is the authority, and a `409` from
`POST`/`PUT` maps to a field-level error on the exact field that collided, so the race between check and
submit resolves correctly instead of throwing a generic error.

---

## ADR-0017 — Node 24 LTS pinned; Angular stays on 21

**Status:** accepted (Phase 1 review addition)

**Context:** the build machine runs Node 25.9.0, an odd-numbered release that never reaches LTS and sits
outside Angular 21's supported range (`^20.19 || ^22.12 || ^24`). Angular 22 exists.

**Decision:** pin Node 24 LTS via `.nvmrc` (`24`) and a `package.json` `engines` field, and document the
requirement in the README as a prerequisite rather than a suggestion. Keep every Angular package —
`@angular/*`, `@angular/material`, `@angular/cdk` — on the 21 major, with matching majors across the three.

**Rationale:** Angular 21 satisfies the assignment's "Angular 21+". Upgrading to 22 for novelty would trade
a verified toolchain for an unverified one on the deadline side of the project, and Material/CDK majors must
track the core major regardless.

**Consequences:** a reviewer on Node 25 gets an engine warning; the README tells them to use Node 24. CI, if
added, pins the same version, so "works on my machine" and "works in the pipeline" mean the same thing.

---

## ADR-0018 — Toolchain and test-library choices made during Phase 2

**Status:** accepted (recorded while implementing, so the reasoning is not lost)

**Solution format `.slnx`.** The .NET 10 SDK emits the XML solution format by default. Keeping it is safe
here because the projects target `net10.0`, so anyone able to build this repository already has an SDK that
understands `.slnx`; converting back would add a legacy file for no reader.

**`NuGet.config` pins one source.** Central package management refuses to restore when several feeds are
configured without source mapping, and the original failure was a private feed present only on the
development machine. The file clears inherited sources and maps every package to nuget.org, which fixes the
restore *and* makes a clone reproducible — plus it removes a dependency-confusion path, since no package can
resolve from an unexpected feed.

**Assertions use xUnit's own `Assert`.** FluentAssertions 8 and later require a paid commercial licence.
Pulling in an alternative assertion library for tests this simple is not worth a dependency or a licence
question in a repository meant to be read by a reviewer.

**Options live in `Infrastructure/Configuration`, not in the API.** `AccessTokenIssuer` and
`RefreshTokenService` consume `JwtOptions` and `RefreshTokenOptions`; putting those classes in the API would
have pointed Infrastructure at the composition root and inverted the dependency the architecture is built on.
API-only settings (`CorsOptions`) do live in the API.

**Audit rows are written before `SaveChanges`, not after.** Entity keys are client-generated version 7
GUIDs, so the target id is known before the insert. That lets the interceptor add its rows to the same
transaction — there is no window in which a change exists without its audit row, and no second save to
recurse through. The constraint this creates is documented in the interceptor: an audited entity with a
database-generated key would have to be handled after the save instead.

---

## Rejected outright

| Idea | Why not |
|---|---|
| MediatR | ADR-0003 |
| Generic `IRepository<T>` over every entity | Hides query intent, invites `GetAll()`, and the assignment names it as an anti-pattern here |
| Microservices, message broker, event sourcing, distributed cache, Kubernetes | Assignment section 5 forbids them absent a measurable benefit; none exists at this scope |
| AutoMapper | Roughly eight mappings, all trivial; hand-written projections keep the SQL shape visible, which matters more than the saved lines |
| Permission/claims table instead of three roles | Speculative generality against a fixed, specified role set |
| ASP.NET Core Identity | Brings its own schema, endpoints and conventions; the assignment asks for a hand-built auth flow, and Identity would obscure exactly the code being assessed. Its `PasswordHasher<T>` is reused on its own |
| Storing the JWT in `localStorage` | ADR-0005 |
| Hard delete with an archive table | Contradicts the soft-delete requirement and loses audit continuity |
| Localizing `Role.Name` in the API | Role names are contract data; translating them at the boundary would break filters and tests |
