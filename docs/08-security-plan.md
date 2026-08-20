# Security plan

## 1. Threat model

**Assets:** user credentials, session tokens, personal data (names, emails), the audit trail, and the role
assignment that governs everything else.

**Actors:** anonymous internet caller; authenticated `ReadOnlyUser`; authenticated `User`; authenticated
`Admin`; an attacker holding a stolen token; a database reader (backup or SQL access).

**Trust boundaries:** browser -> API (untrusted input, untrusted claims until validated); API -> database
(parameterised only); API -> logs and audit store (must never receive secrets).

| # | Threat | Actor | Control | Residual |
|---|---|---|---|---|
| T-01 | Credential stuffing / brute force | anonymous | 5-failure lockout for 15 min (per account) + fixed-window rate limit on `/api/auth/*` (per IP) — the two answer different questions and both are kept | Distributed low-rate attempts remain possible; logged and visible in failed-login events |
| T-01b | Username enumeration through login responses | anonymous | One `INVALID_CREDENTIALS` response for unknown user, wrong password and soft-deleted account; password verification always runs, against a dummy hash when the user does not exist, so timing does not separate the cases. `ACCOUNT_LOCKED` is returned **only** when the supplied password was correct (ADR-0006) | A caller who already knows a valid password learns that the account is locked — no new information to them |
| T-02 | Password disclosure from a database leak | db reader | PBKDF2-HMAC-SHA512, 210k iterations, per-user salt, ASP.NET v3 format | Offline cracking of weak passwords; mitigated by the 12-char policy |
| T-03 | Token theft via XSS | anonymous | Access token in memory only, never in `localStorage`/`sessionStorage`; refresh token `httpOnly`; Angular escaping; CSP | An XSS foothold can still call the API as the user while the page is open. Accepted; the fix is to not have XSS, which is why `bypassSecurityTrust*` is banned by lint rule |
| T-04 | Token replay after logout or demotion | holder of a stolen access token | 15-minute access-token lifetime; security-stamp rotation and refresh revocation on password/role change | Up to 15 minutes of validity for an already-issued access token. Documented, and the alternative (per-request token revocation lookup) is rejected as a database hit on every call |
| T-05 | Refresh-token theft | anonymous | Stored as SHA-256; `httpOnly`+`Secure`+`SameSite=Strict`; rotation on every use; reuse of a rotated token revokes the whole chain and logs at `Warning` | Theft plus immediate use before the legitimate client refreshes |
| T-06 | Privilege escalation | `User`, `ReadOnlyUser` | Policies, BR-04, BR-03, closed role set, signed claims — see [07-authorization-matrix.md](07-authorization-matrix.md) | none identified |
| T-07 | IDOR | `User` | `/users/me` has no id segment; `/users/{id}` is Admin-only | none identified |
| T-08 | Mass assignment | any authenticated | Scoped request records; `UnmappedMemberHandling = Disallow`; entities never model-bound | none identified |
| T-09 | SQL injection | any | EF Core parameterisation everywhere; sorting via a dictionary whitelist; no raw SQL, no string interpolation into queries | none identified |
| T-10 | Soft-delete bypass | any | Global query filter; `IgnoreQueryFilters()` confined to one repository method with two justified callers and no client-controlled parameter; deleted rows behind an Admin-only route (ADR-0004) | none identified |
| T-16 | Lost update — one Admin silently overwrites another's edit, and the audit trail presents both as deliberate | `Admin` | `rowversion` optimistic concurrency on `User`; stale writes rejected with `409 RESOURCE_MODIFIED` (ADR-0013) | An Admin who reloads and re-applies their change still wins; that is a policy outcome, not a defect |
| T-17 | Username reuse after soft delete confuses accountability | `Admin` | Audit identity is the immutable `UserId`; usernames appear only as snapshots (`EntityDisplayName`, `PerformedByUsername`) (ADR-0009) | none identified |
| T-11 | Audit tampering | `Admin` | No update/delete endpoints; entity has no public setters; append-only table | An Admin with direct SQL access can still alter rows. Out of application scope; the mitigation is database permissions, noted as a deployment requirement |
| T-12 | Information disclosure through errors | any | ProblemDetails only; generic 500; generic auth failures; `Server` header suppressed; Swagger off by default in Production | Timing differences between "unknown user" and "wrong password" are reduced by always running a hash verification against a dummy hash, but not eliminated |
| T-13 | Secret leakage into source control | insider/accident | `appsettings.example.json` with placeholders; real values in User Secrets or environment; `.gitignore` covers env files; `JwtOptions.Key` validated at startup and refused if it is the example value | none identified |
| T-14 | CSRF | anonymous | The API authenticates with a bearer header, which a cross-site form cannot set. The refresh cookie is `SameSite=Strict` and scoped to `Path=/api/auth`, and refresh alone yields nothing without the response body reaching the attacker's origin (blocked by CORS) | none identified |
| T-15 | PII over-collection in logs | insider | Serilog destructuring policy redacts password fields; request bodies are not logged; audit stores names/emails because that is the feature's purpose | Audit rows contain personal data by design; retention policy is a deployment decision, flagged as a known limitation |

## 2. OWASP Top 10 (2021) coverage

| Category | Position |
|---|---|
| A01 Broken Access Control | Primary risk for this module. Covered by policies, route shape, business rules and a parameterised authorization test matrix (T-06, T-07, T-10) |
| A02 Cryptographic Failures | ASP.NET `PasswordHasher` (no custom crypto); refresh tokens hashed at rest; HTTPS enforced with HSTS in Production; JWT signed HS256 with a >= 32-byte key validated at startup |
| A03 Injection | EF Core parameterisation; sort whitelist; Angular template escaping; no `innerHTML`, no `bypassSecurityTrust*` |
| A04 Insecure Design | Deliberate design decisions recorded as ADRs, including what was rejected; last-Admin and self-delete rules exist because the design considered the failure modes |
| A05 Security Misconfiguration | Options validated with `ValidateOnStart`; security headers middleware; Swagger and detailed errors off outside Development; CORS names one explicit origin, never `*` with credentials |
| A06 Vulnerable Components | Central package management, pinned versions, `dotnet list package --vulnerable` and `npm audit` documented as part of the pre-release check |
| A07 Identification & Authentication Failures | Lockout, rate limiting, generic failure messages, short access tokens, rotating refresh tokens, reuse detection, password policy |
| A08 Software & Data Integrity Failures | Migrations are the single schema authority and `001_schema.sql` is generated from them; seed hashes are literals, not runtime-generated secrets |
| A09 Logging & Monitoring Failures | Structured Serilog with correlation ids; explicit events for failed login, role change, delete, restore, refresh-reuse; audit trail independent of logs |
| A10 SSRF | Not applicable: the API makes no outbound HTTP requests |

## 3. Secret management

| Secret | Development | Production |
|---|---|---|
| Connection string | User Secrets (`dotnet user-secrets`) | environment variable / orchestrator secret |
| `Jwt:Key` | User Secrets, >= 32 bytes | environment variable; startup fails if missing, short, or equal to the example placeholder |
| SQL `sa` password (Compose only) | `.env` file, gitignored, with `.env.example` committed | not used |

`appsettings.example.json` is committed and contains placeholders such as
`"Key": "REPLACE_WITH_A_32_BYTE_MINIMUM_SECRET"`. `appsettings.Development.json` and
`appsettings.Production.json` are gitignored. A startup guard refuses to boot on a placeholder key outside
Development, so a misconfigured deployment fails loudly instead of running on a known key.

## 4. Password policy

12-128 characters, requiring an uppercase letter, a lowercase letter and a digit. Symbols allowed, not
required — length beats symbol classes, and mandating symbols pushes users toward predictable substitutions.
No composition rule beyond that, no forced rotation. Enforced by one validator shared by create-user and
change-password, driven by `PasswordPolicyOptions` so the rule is configuration, not scattered literals.

Demo credentials satisfy the policy and are documented in the README as development-only, alongside an
explicit statement that they must not be used in any deployed environment.

## 5. Security headers and CORS

```text
Content-Security-Policy: default-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Strict-Transport-Security: max-age=31536000; includeSubDomains   (Production only)
```

CORS: a single named policy listing the SPA origin explicitly, with `AllowCredentials` (required for the
refresh cookie) and only the methods and headers actually used. `AllowAnyOrigin` with credentials is
invalid and is never attempted.

## 6. What gets logged, and what never does

**Logged:** correlation id, method, path, status, duration, authenticated user id and username, client IP,
and named security events (`LoginFailed`, `LoginSucceeded`, `RoleChanged`, `UserDeleted`, `UserRestored`,
`RefreshTokenReuseDetected`, `AccountLocked`).

**Never logged:** passwords, password hashes, access tokens, refresh tokens (raw or hashed), cookies,
`Authorization` headers, or full request bodies for auth endpoints.

The guarantee comes from construction rather than filtering: no code path passes a credential to a logger, and
request bodies are not logged at all. Because that is one helpful log line away from being false, it is
asserted rather than trusted — `SensitiveDataLoggingTests` runs sign-in (failed, unknown user, successful,
locked out) and the password change through a capturing logger that records **structured values as well as
rendered text**, and asserts the password, the stored hash, the access token and the refresh token are all
absent. Structured values matter here: a credential can leak through a template parameter that never appears
in a console line but does reach a JSON sink.

The same prohibition applies to the audit trail, where it is specified normatively rather than left to
judgement: [13-audit-policy.md](13-audit-policy.md) lists what is captured, what is redacted to `"***"`, and
what must never be persisted in any column. Its "never persisted" list is a single constant shared by the
interceptor and the tests, so the policy and the implementation cannot diverge without a test failing.

## 7. Pre-release security checklist (executed in Phase 10)

- [ ] `dotnet list package --vulnerable --include-transitive` clean, `npm audit --omit=dev` clean
- [ ] No secret in git history (`git log -p` grep for key patterns; scan of `appsettings*.json`)
- [ ] Authorization matrix test green for all three roles across every mutating endpoint
- [ ] IDOR and mass-assignment tests green
- [ ] Soft-delete authorization tests green: `GET /api/users/deleted` returns `403` for `User` and `ReadOnlyUser`, and no deleted row appears in any response to a non-Admin
- [ ] Redaction test green (no password material in logs or audit rows), plus the end-to-end password-change audit test
- [ ] Concurrency test green: a stale `rowVersion` returns `409 RESOURCE_MODIFIED` and leaves the row untouched
- [ ] Refresh-token tests green: rotation, family revocation on reuse detection, revocation on password/role change
- [ ] Filtered unique indexes verified: a soft-deleted user's username and email can be reused, and a restore that collides returns `409`
- [ ] Error responses reviewed: no stack trace, SQL text, connection string or config in any 4xx/5xx body
- [ ] `Jwt:Key` placeholder guard verified by starting the API in the Production environment without a key
- [ ] Soft-delete filter verified by querying every list endpoint as each role
- [ ] Security headers verified on a real response
- [ ] Residual risks in this document re-read and still accurate
