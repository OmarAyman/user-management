# Audit policy

This document is **normative**. The audit interceptor is written against it, and the tests named in section
7 enforce it. Changing what is audited means changing this document first.

## 1. Purpose and scope

The audit trail answers one question after the fact: **who changed what, when, from where, and what did the
value change from and to.** It is not an application log, not a metrics stream and not a debugging aid — the
Serilog output covers those. Two separate stores with two separate purposes, because an audit trail that
doubles as a log ends up either too noisy to review or too sparse to trust.

## 2. Audited entities

| Entity | Audited | Rationale |
|---|---|---|
| `User` | **Yes** — every insert, update, soft delete, restore and role change | The asset the module exists to manage; role changes are the highest-privilege event in the system |
| `Role` | No | Fixed, seeded reference data with no runtime write path. If role CRUD is ever added, it becomes audited and this row changes |
| `RefreshToken` | **No** — deliberately | Rows are created and rotated on a schedule the user does not control; auditing them would flood the trail with mechanical events and, worse, put token lifecycle data next to the user data an auditor reads. Token events are security *log* events instead (section 6) |
| `AuditLog` | No — and cannot be | Auditing the audit table is a recursion with no reader. Immutability is enforced structurally instead (section 5) |

## 3. Audited operations

| Action | `AuditAction` | Trigger |
|---|---|---|
| User created | `Insert` | An `Added` `User` entry reaches `SaveChanges` |
| User updated | `Update` | A `Modified` `User` entry with at least one audited property changed |
| User soft-deleted | `Delete` | `IsDeleted` transitions `false -> true` |
| User restored | `Restore` | `IsDeleted` transitions `true -> false` |
| User role changed | `RoleChange` | `RoleId` changed — emitted **in addition to** the `Update` row |

Notes that matter:

- A soft delete produces a `Delete` row, **not** an `Update` row, even though it is physically an update.
  The trail records intent, and "deleted" is what a reviewer searches for.
- A role change produces two rows: the `Update` covering the whole change set, and a dedicated `RoleChange`
  carrying only the role delta. Duplication is intentional — privilege changes must be findable with a
  single-column filter, not by diffing JSON.
- A save that changes nothing audited (for example only `LastLoginAt`) produces **no** audit row. See
  section 4 for the excluded list.

## 4. Field policy

### 4.1 Captured on every audit row

| Field | Source | Notes |
|---|---|---|
| `Id` | identity | `bigint`, append-only |
| `EntityName` | change tracker | e.g. `User` |
| `EntityId` | primary key | The target's **`UserId`** — the immutable identity, never a username (ADR-0009) |
| `EntityDisplayName` | entity snapshot | The target's username *as it was* at the time of the action; a readability aid, never a key |
| `Action` | interceptor | `tinyint` + check constraint |
| `PerformedByUserId` | `ICurrentUserService` | `null` only for seeding and system operations |
| `PerformedByUsername` | `ICurrentUserService` | Snapshot, same reasoning as `EntityDisplayName` |
| `Timestamp` | `IDateTimeProvider` | UTC `DateTimeOffset` |
| `IpAddress` | `IClientInfoProvider` | `unknown` when there is no HTTP context (seeder, tests) |
| `OldValues` | change tracker original values | JSON object, changed properties only, `null` on insert |
| `NewValues` | change tracker current values | JSON object, changed properties only, `null` on delete-as-purge (not used here) |
| `CorrelationId` | correlation middleware | Ties the row to the request log |

`OldValues`/`NewValues` contain **only the properties that actually changed**. Serialising the whole entity
would bury the one field that moved and would put unchanged sensitive columns into storage for no reason.

### 4.2 Audited properties of `User`

`Username`, `Email`, `FirstName`, `LastName`, `RoleId`, `IsDeleted`, `DeletedAt`, `DeletedBy`.

### 4.3 Excluded properties — changes do not create or appear in audit rows

| Property | Why excluded |
|---|---|
| `CreatedAt`, `CreatedBy`, `LastModifiedAt`, `LastModifiedBy` | Already implied by the audit row's own `Timestamp` and actor; recording them duplicates the row into itself |
| `RowVersion` | Engine-maintained, meaningless to a reader |
| `LastLoginAt` | Changes on every login; would swamp the trail. Login is a security *log* event instead |
| `FailedLoginAttempts`, `LockoutEndAt` | Mechanical counters, high churn; lockout is a security log event |

### 4.4 Redacted — the change is recorded, the value never is

| Property | Stored as |
|---|---|
| `PasswordHash` | `"***"` in both `OldValues` and `NewValues` |
| `SecurityStamp` | `"***"` |

Redaction rather than exclusion, because *that* a password changed is exactly what an auditor needs to see;
*what* it changed to is exactly what they must not. A password change therefore produces
`{"passwordHash": "***"}` on both sides — the event is visible, the material is not.

### 4.5 Never persisted — under any circumstance, in any column

- Plaintext passwords (they exist only as a request field in memory and are never attached to an entity)
- Password hashes (redacted per 4.4 — the literal hash never reaches the JSON)
- JWT access tokens
- Refresh tokens, raw or hashed
- Cookies, `Authorization` header values, any credential or secret
- Full request or response bodies

This list is a single constant in code (`AuditRedaction.NeverPersisted`) consumed by both the interceptor
and the tests, so the document, the implementation and the assertions cannot drift apart.

## 5. Immutability

| Layer | Control |
|---|---|
| Domain | `AuditLog` has no public setters after construction and no mutating methods |
| Persistence | No repository method updates or deletes an audit row; the entity is registered without a delete path |
| API | No `PUT`, `PATCH` or `DELETE` route exists under `/api/audit-logs` |
| Authorization | Read requires `Policies.ViewAuditLogs` (Admin) |
| Test | An integration test performs an update-then-delete sequence and asserts every previously written audit row is byte-identical afterwards |

**Out of application scope:** an operator with direct SQL access can still alter rows. The mitigation is
database permissions — the application's SQL principal needs `INSERT` and `SELECT` on `AuditLogs` and
nothing else. This is stated as a deployment requirement in the security plan (T-11) rather than pretended
away.

## 6. Security log events (not audit rows)

These go to Serilog with a stable event name, never to `AuditLogs`, because they are not entity changes:

| Event | Fields |
|---|---|
| `LoginSucceeded` | user id, username, IP, correlation id |
| `LoginFailed` | attempted username, IP, failure category (`UnknownUser`, `BadPassword`, `LockedOut`, `Deleted`), correlation id |
| `AccountLocked` | user id, username, IP, lockout expiry |
| `RefreshTokenRotated` | user id, family id |
| `RefreshTokenReuseDetected` | user id, family id, IP — logged at `Warning` |
| `RefreshTokensRevoked` | user id, reason, count |
| `RoleChanged` | actor, target, old role, new role, IP — logged **in addition** to the `RoleChange` audit row, so a log-only reader still sees privilege movement |
| `UserDeleted`, `UserRestored` | actor, target, IP |

No event in this table carries a password, a hash or a token. What enforces that is the test in section 7,
not a Serilog destructuring policy - an earlier version of this document claimed one, and it did not exist.
`SensitiveDataLoggingTests` drives four sign-in outcomes and a password change through a capturing logger and
asserts that the password, the stored hash, the access token and the refresh token appear in neither the
rendered message nor any structured value. Structured values matter separately: a credential can leak through
a template parameter that never reaches a console line but does reach a JSON sink.

## 7. Enforcing tests

| Test | Asserts |
|---|---|
| `Audit_row_written_for_insert_update_delete_restore_and_role_change` | Section 3 in full |
| `RoleChange_emits_a_dedicated_row_in_addition_to_update` | Section 3 |
| `SoftDelete_is_audited_as_Delete_not_Update` | Section 3 |
| `Audit_captures_only_changed_properties` | Section 4.1 |
| `Audit_excludes_stamp_rowversion_and_login_counters` | Section 4.3 |
| `Audit_redacts_password_hash_and_security_stamp` | Section 4.4 |
| `Password_change_flow_writes_no_password_material_to_any_audit_row` | Section 4.5, end to end through HTTP |
| `Audit_rows_never_contain_token_material` | Section 4.5 |
| `Audit_rows_are_immutable_across_subsequent_operations` | Section 5 |
| `Audit_endpoint_rejects_non_admin_roles` | Section 5 |
| `Security_log_events_never_contain_password_or_token_values` | Section 6, via a capturing Serilog sink |
| `Audit_row_records_entity_id_display_name_ip_and_correlation_id` | Sections 4.1 and ADR-0009 |

## 8. Retention

Not implemented, and deliberately so: retention is a policy decision belonging to whoever operates the
system, and hard-coding a window would be a guess. The table is designed for it — `Timestamp` is indexed
descending, so an age-based archive or purge job is a single ranged delete. Recorded as a known limitation
in the README, together with the note that audit rows contain personal data (names, emails, IP addresses)
and therefore fall under whatever data-protection regime applies to the deployment.
