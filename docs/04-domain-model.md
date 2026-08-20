# Domain model

## 1. Entity overview

```text
        +-----------+                +----------------+
        |   Role    |1              *|      User      |
        |-----------|<---------------|----------------|
        | Id (int)  |     RoleId     | Id (guid)      |
        | Name      |                | Username       |
        +-----------+                | Email          |
                                     | FirstName      |
                                     | LastName       |
                                     | PasswordHash   |
                                     | RoleId         |
                                     | IsDeleted      |
                                     | RowVersion     |
                                     | audit fields   |
                                     +--------+-------+
                                              |1
                        +---------------------+---------------------+
                        |*                                        *|
              +---------v----------+                    +----------v---------+
              |   RefreshToken     |                    |     AuditLog       |
              |--------------------|                    |--------------------|
              | Id (guid)          |                    | Id (bigint)        |
              | UserId             |                    | EntityName         |
              | FamilyId           |                    | EntityId           |
              | TokenHash          |                    | EntityDisplayName  |
              | ReplacedByTokenId  |                    | Action             |
              | ExpiresAt          |                    | PerformedByUserId  |
              | RevokedAt          |                    | PerformedByUsername|
              | RevocationReason   |                    | Timestamp          |
              | CreatedByIp        |                    | IpAddress          |
              +---------+----------+                    | OldValues (json)   |
                        |  self-FK                      | NewValues (json)   |
                        +--> ReplacedByTokenId          | CorrelationId      |
                                                        +--------------------+
```

`AuditLog.PerformedByUserId` is a **weak reference** — a guid column with an index but no cascading FK
behaviour that could ever delete history. `AuditLog.EntityId` is a string so the table can audit entities
with different key types without a schema change, and it always holds the target's **immutable id**
(ADR-0009) — never a username. `RefreshToken.ReplacedByTokenId` is a self-referencing FK, so a rotation
chain is navigable in SQL.

## 2. `User`

| Property | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Sequential guid (`NEWSEQUENTIALID` equivalent) to keep the clustered index from fragmenting |
| `Username` | `string(50)` | Required, unique **among active users** (filtered index, ADR-0009), immutable after creation, case-insensitive comparison |
| `Email` | `string(256)` | Required, unique among active users, RFC-shaped, stored as entered, compared case-insensitively |
| `FirstName` | `string(100)` | Required |
| `LastName` | `string(100)` | Required |
| `PasswordHash` | `string(500)` | Required; PBKDF2 (ASP.NET v3 format); never leaves the Application layer |
| `SecurityStamp` | `Guid` | Rotated on password change or role change; invalidates outstanding refresh tokens |
| `RoleId` | `int` | Required FK to `Roles` |
| `IsDeleted` | `bool` | Default `false` |
| `DeletedAt` | `DateTimeOffset?` | Set with `IsDeleted` |
| `DeletedBy` | `string(50)?` | Actor username |
| `FailedLoginAttempts` | `int` | Reset on success |
| `LockoutEndAt` | `DateTimeOffset?` | Set when the failure threshold is crossed |
| `LastLoginAt` | `DateTimeOffset?` | Informational, shown to Admins |
| `CreatedAt` | `DateTimeOffset` | Set by interceptor, UTC |
| `CreatedBy` | `string(50)` | Actor username, or `system` for seeded rows |
| `LastModifiedAt` | `DateTimeOffset?` | Set by interceptor |
| `LastModifiedBy` | `string(50)?` | Actor username |
| `RowVersion` | `byte[]` | SQL Server `rowversion`; engine-maintained optimistic concurrency token (ADR-0013). Never assigned in code, exposed to clients as base64 |

### Behaviour on the entity (not in handlers)

```text
User.Create(username, email, firstName, lastName, passwordHash, roleId)  -> factory, guards required fields
User.ChangeRole(newRoleId)          -> no-op if unchanged; rotates SecurityStamp
User.UpdateProfile(firstName, lastName, email)
User.SetPasswordHash(hash)          -> rotates SecurityStamp
User.SoftDelete(actor, now)         -> throws if already deleted
User.Restore(actor, now)            -> throws if not deleted
User.RecordFailedLogin(now, policy) -> increments, sets LockoutEndAt at threshold
User.RecordSuccessfulLogin(now)     -> clears counters, stamps LastLoginAt
User.IsLockedOut(now)               -> LockoutEndAt > now
```

Putting these on the entity means the invariants hold no matter which use case calls them, and the domain
unit tests need no mocks at all.

### Uniqueness applies to active users only

`Username` and `Email` are unique **among rows where `IsDeleted = 0`**, via SQL Server filtered unique
indexes. Soft-deleting a user returns their username and email to the pool immediately.

Audit clarity does not depend on that namespace, because audit identity is `UserId`:

```text
UserId A123, Username "john"  -> soft-deleted    audit rows: EntityId = A123, EntityDisplayName = "john"
UserId B456, Username "john"  -> created later   audit rows: EntityId = B456, EntityDisplayName = "john"
```

Two accounts may share a name over time; no audit row is ambiguous, because every row names the immutable
`UserId` it refers to and treats the username purely as a display snapshot. Filtering a user's history
filters on `EntityId`, never on a name. Full reasoning, including why the opposite decision was reversed, is
in ADR-0009.

**The failure mode this introduces** is restore: a deleted user's username may have been taken by an active
user in the meantime. `Restore` therefore re-checks availability first and fails with `409` rather than
letting the filtered index reject the write (BR-17).

## 3. `Role`

| Property | Type | Rules |
|---|---|---|
| `Id` | `int` | Fixed seed values: `1 = Admin`, `2 = User`, `3 = ReadOnlyUser` |
| `Name` | `string(50)` | Required, unique |

Roles are persisted rows, referenced by the `RoleIds`/`RoleNames` constants — never by inline literals.
The role set is closed: there is no role CRUD API, because the assignment fixes three roles and the
authorization policies are written against them. `GET /api/roles` is read-only, feeding the filter
dropdown and the role selector.

## 4. `AuditLog`

| Property | Type | Notes |
|---|---|---|
| `Id` | `long` | Identity |
| `EntityName` | `string(100)` | e.g. `User` |
| `EntityId` | `string(64)` | The target's immutable key — for users, the `UserId`. This is the audit identity (ADR-0009) |
| `EntityDisplayName` | `string(100)?` | The target's username as it was at the time of the action; readability only, never a key |
| `Action` | `AuditAction` | `Insert`, `Update`, `Delete`, `Restore`, `RoleChange` — stored as a `tinyint` with a check constraint |
| `PerformedByUserId` | `Guid?` | Null only for system/seed operations |
| `PerformedByUsername` | `string(50)` | Denormalised on purpose: history must stay readable even if the actor is later deleted |
| `Timestamp` | `DateTimeOffset` | UTC |
| `IpAddress` | `string(45)` | IPv6-capable length |
| `OldValues` | `nvarchar(max)?` | JSON, redacted |
| `NewValues` | `nvarchar(max)?` | JSON, redacted |
| `CorrelationId` | `string(64)?` | Ties the row to the request log |

**Immutability:** the entity has no public setters after construction, the repository exposes no update or
delete, and there is no endpoint that mutates audit rows. A dedicated integration test asserts that no
audit row changes across an update-and-delete sequence.

**Redaction and exclusion** are specified normatively in [13-audit-policy.md](13-audit-policy.md): captured
fields, audited properties, excluded properties (`RowVersion`, stamps, login counters), redacted values
(`PasswordHash`, `SecurityStamp` -> `"***"`) and the never-persisted list (plaintext passwords, hashes,
access tokens, refresh tokens, credentials). Redacted-but-changed properties are recorded as `"***"` so a
reviewer sees *that* a password changed without learning anything about it.

## 5. `RefreshToken`

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `UserId` | `Guid` | FK, cascade delete is irrelevant since users are only soft-deleted |
| `FamilyId` | `Guid` | One family per login; every rotation inherits it. The revocation unit |
| `TokenHash` | `string(64)` | SHA-256 hex of a 256-bit random value; the raw token exists only in the cookie |
| `ReplacedByTokenId` | `Guid?` | Self-referencing FK to the successor row; makes the rotation chain navigable and reuse detectable |
| `ExpiresAt` | `DateTimeOffset` | 7 days |
| `CreatedAt` / `CreatedByIp` | | |
| `RevokedAt` / `RevokedByIp` | `DateTimeOffset?` / `string(45)?` | |
| `RevocationReason` | `RevocationReason?` | `Rotated`, `ReuseDetected`, `Logout`, `PasswordChanged`, `RoleChanged`, `UserDeleted`, `Expired` |

Storing only the hash means a database disclosure does not hand over usable sessions, and the raw token is
never written to a log. Presenting a token that already has `ReplacedByTokenId` set means two clients hold
tokens from one lineage — theft — so the **whole family** is revoked with `RevocationReason = ReuseDetected`
and the event is logged at `Warning`. Revoking the family rather than every token the user owns means a
compromise on one device does not sign them out everywhere. Password change, role change and deletion revoke
*all* families, because those events invalidate every session regardless of lineage. Full design in ADR-0005.

## 6. Business rules

| ID | Rule | Enforced where | API result when violated |
|---|---|---|---|
| BR-01 | A user cannot delete their own account | `DeleteUserCommandHandler` | `403` `CANNOT_DELETE_SELF` |
| BR-02 | The last active Admin cannot be deleted | `DeleteUserCommandHandler` (count of active Admins) | `409` `LAST_ADMIN_CANNOT_BE_REMOVED` |
| BR-03 | The last active Admin cannot be demoted | `UpdateUserCommandHandler` | `409` `LAST_ADMIN_CANNOT_BE_REMOVED` |
| BR-04 | Nobody changes their own role, Admins included | `/users/me` DTO has no role; `UpdateUserCommandHandler` rejects a self role delta | `422` `CANNOT_CHANGE_OWN_ROLE` |
| BR-05 | A soft-deleted user cannot authenticate | `LoginCommandHandler` (ignores query filter, then rejects) | `401` `INVALID_CREDENTIALS` (deliberately indistinguishable) |
| BR-06 | A soft-deleted user's existing tokens stop working | Refresh revokes on `IsDeleted`; access token lifetime is 15 minutes | `401` |
| BR-07 | Deleting an already-deleted user is a conflict | `User.SoftDelete` guard | `409` `USER_ALREADY_DELETED` |
| BR-08 | Restoring a non-deleted user is a conflict | `User.Restore` guard | `409` `USER_NOT_DELETED` |
| BR-09 | Username and email are unique **among active users** | Handler pre-check qualified with `IsDeleted = 0`, plus the filtered unique index as the true guard | `409` `USERNAME_ALREADY_EXISTS` / `EMAIL_ALREADY_EXISTS` |
| BR-10 | Username is immutable after creation | `UpdateUserRequest` has no username field | n/a (field absent) |
| BR-11 | Password must meet policy: 12-128 chars, upper + lower + digit | `PasswordPolicyOptions` + validator, shared by create and change-password | `400` with `errors.password` |
| BR-12 | 5 failed logins lock the account for 15 minutes | `User.RecordFailedLogin` | `401` `INVALID_CREDENTIALS` for a wrong password; `401 ACCOUNT_LOCKED` only when the password was correct (ADR-0006) |
| BR-13 | Changing a password revokes all refresh tokens | `ChangeMyPasswordCommandHandler` | n/a |
| BR-14 | A role change revokes all refresh tokens for that user | `UpdateUserCommandHandler` | n/a |
| BR-15 | Only an Admin may list deleted users | A separate route, `GET /api/users/deleted`, behind `Policies.ManageUsers`; `GET /api/users` has no soft-delete parameter at all | `403` |
| BR-16 | A stale update is rejected, not silently applied | `User.RowVersion` concurrency token; EF throws `DbUpdateConcurrencyException` | `409` `RESOURCE_MODIFIED` |
| BR-17 | Restore requires the username and email to still be free | `RestoreUserCommandHandler` re-checks availability among active users before saving | `409` `USERNAME_ALREADY_EXISTS` / `EMAIL_ALREADY_EXISTS` |

### Two rules worth calling out

**BR-12 discloses lockout only to someone who proved they know the password.** Four outcomes, one of which
is distinguishable:

| Situation | Response |
|---|---|
| Unknown username | `401 INVALID_CREDENTIALS` |
| Known username, wrong password (locked or not) | `401 INVALID_CREDENTIALS` |
| Known username, **correct** password, locked | `401 ACCOUNT_LOCKED` + retry time |
| Known username, correct password, soft-deleted | `401 INVALID_CREDENTIALS` |

An enumerating attacker without the password cannot separate those cases; a legitimate user who mistyped
five times and then typed correctly is told to wait rather than told, misleadingly, that their password is
wrong. Password verification runs even for unknown usernames (against a fixed dummy hash) so timing does not
separate them either. Rationale in ADR-0006.

**BR-16 turns a silent data loss into a visible conflict.** Two Admins editing one user is routine; without
a concurrency token the second save overwrites the first and the audit trail shows both as deliberate,
sequential edits. `RowVersion` makes the second save fail with `409 RESOURCE_MODIFIED` instead.

**BR-02/BR-03 are checked inside the same transaction as the mutation** and rely on a re-read of the active
Admin count. Under concurrent demotion of two Admins, the second transaction re-reads and fails, so the
system cannot reach zero Admins through a race.

## 7. Invariant test coverage

Each rule above maps to at least one unit test named after it, plus an integration test where the rule is
observable through HTTP. See [10-testing-plan.md](10-testing-plan.md).
