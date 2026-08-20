# Database model

Target: **SQL Server 2022** (2019 compatible). Created by EF Core migrations; `database/001_schema.sql` is
generated from the same migration (`dotnet ef migrations script`) so the two can never drift.

## 1. Tables

### `Roles`

| Column | Type | Constraints |
|---|---|---|
| `Id` | `int` | PK, **not** identity — fixed ids are seeded so migrations and scripts agree |
| `Name` | `nvarchar(50)` | NOT NULL, `UQ_Roles_Name` |

Seeded: `(1, 'Admin')`, `(2, 'User')`, `(3, 'ReadOnlyUser')`.

### `Users`

| Column | Type | Constraints / default |
|---|---|---|
| `Id` | `uniqueidentifier` | PK (non-clustered). Generated in code as a **version 7 GUID** (`Guid.CreateVersion7()`), which is time-ordered like `NEWSEQUENTIALID()` but known before the insert - so the audit interceptor can write its rows in the same transaction rather than after the save |
| `Username` | `nvarchar(50)` | NOT NULL, `UQ_Users_ActiveUsername` (filtered) |
| `Email` | `nvarchar(256)` | NOT NULL, `UQ_Users_ActiveEmail` (filtered) |
| `FirstName` | `nvarchar(100)` | NOT NULL |
| `LastName` | `nvarchar(100)` | NOT NULL |
| `PasswordHash` | `nvarchar(500)` | NOT NULL |
| `SecurityStamp` | `uniqueidentifier` | NOT NULL |
| `RoleId` | `int` | NOT NULL, FK -> `Roles(Id)` `ON DELETE NO ACTION` |
| `IsDeleted` | `bit` | NOT NULL, `DEFAULT 0` |
| `DeletedAt` | `datetimeoffset(7)` | NULL |
| `DeletedBy` | `nvarchar(50)` | NULL |
| `FailedLoginAttempts` | `int` | NOT NULL, `DEFAULT 0` |
| `LockoutEndAt` | `datetimeoffset(7)` | NULL |
| `LastLoginAt` | `datetimeoffset(7)` | NULL |
| `CreatedAt` | `datetimeoffset(7)` | NOT NULL |
| `CreatedBy` | `nvarchar(50)` | NOT NULL |
| `LastModifiedAt` | `datetimeoffset(7)` | NULL |
| `LastModifiedBy` | `nvarchar(50)` | NULL |
| `RowVersion` | `rowversion` | NOT NULL, engine-maintained concurrency token (ADR-0013) |

Check constraints: `CK_Users_DeletedConsistency` ensures `IsDeleted = 0` implies `DeletedAt IS NULL`, and
`IsDeleted = 1` implies `DeletedAt IS NOT NULL`. A soft-delete flag that can disagree with its timestamp is
a reporting bug waiting to happen.

`RowVersion` is a SQL Server `rowversion` (8 bytes, monotonic per database). Nothing in the application
assigns it; EF Core reads it, sends it back in the `WHERE` clause of every `UPDATE`, and raises
`DbUpdateConcurrencyException` when zero rows match.

Clustering: `Id` is a **non-clustered** PK; the clustered index is `CIX_Users_CreatedAt (CreatedAt, Id)`,
because the default list ordering is newest-first and range scans on that key are the hot path. Sequential
guids keep insert locality reasonable either way.

### `AuditLogs`

| Column | Type | Constraints |
|---|---|---|
| `Id` | `bigint` | PK IDENTITY, clustered (append-only, no page splits) |
| `EntityName` | `nvarchar(100)` | NOT NULL |
| `EntityId` | `nvarchar(64)` | NOT NULL — the target's immutable id, never a username |
| `EntityDisplayName` | `nvarchar(100)` | NULL — username snapshot at the time of the action |
| `Action` | `tinyint` | NOT NULL, `CK_AuditLogs_Action` in (0..4) |
| `PerformedByUserId` | `uniqueidentifier` | NULL (system operations) |
| `PerformedByUsername` | `nvarchar(50)` | NOT NULL |
| `Timestamp` | `datetimeoffset(7)` | NOT NULL |
| `IpAddress` | `nvarchar(45)` | NOT NULL |
| `OldValues` | `nvarchar(max)` | NULL, JSON |
| `NewValues` | `nvarchar(max)` | NULL, JSON |
| `CorrelationId` | `nvarchar(64)` | NULL |

No FK to `Users`. History must survive regardless of what happens to the actor row, and an FK on an
append-only audit table only adds write cost and a delete-order dependency.

### `RefreshTokens`

| Column | Type | Constraints |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `UserId` | `uniqueidentifier` | NOT NULL, FK -> `Users(Id)` `ON DELETE NO ACTION` |
| `FamilyId` | `uniqueidentifier` | NOT NULL — one family per login, inherited by every rotation |
| `TokenHash` | `char(64)` | NOT NULL, `UQ_RefreshTokens_TokenHash` |
| `ReplacedByTokenId` | `uniqueidentifier` | NULL, self-FK -> `RefreshTokens(Id)` `ON DELETE NO ACTION` |
| `ExpiresAt` | `datetimeoffset(7)` | NOT NULL |
| `CreatedAt` | `datetimeoffset(7)` | NOT NULL |
| `CreatedByIp` | `nvarchar(45)` | NOT NULL |
| `RevokedAt` | `datetimeoffset(7)` | NULL |
| `RevokedByIp` | `nvarchar(45)` | NULL |
| `RevocationReason` | `tinyint` | NULL, `CK_RefreshTokens_RevocationReason` in (0..6) |

`ON DELETE NO ACTION` on both FKs: users are only ever soft-deleted, so a cascade would never fire, and a
self-referencing cascade is rejected by SQL Server anyway.

## 2. Indexes

| Index | Table | Definition | Serves |
|---|---|---|---|
| `UQ_Users_ActiveUsername` | Users | UNIQUE (`Username`) `WHERE IsDeleted = 0` | login lookup, uniqueness among active users (BR-09, ADR-0009) |
| `UQ_Users_ActiveEmail` | Users | UNIQUE (`Email`) `WHERE IsDeleted = 0` | uniqueness among active users, email search seek |
| `IX_Users_Username_All` | Users | (`Username`) — non-unique, unfiltered | login must read soft-deleted rows to refuse them (BR-05), and the filtered index cannot serve that lookup |
| `CIX_Users_CreatedAt` | Users | CLUSTERED (`CreatedAt`, `Id`) | default sort, paging range scan |
| `IX_Users_RoleId_IsDeleted` | Users | (`RoleId`, `IsDeleted`) INCLUDE (`Username`,`Email`,`FirstName`,`LastName`,`CreatedAt`) | role filter + soft-delete filter, covering the list projection |
| `IX_Users_IsDeleted` | Users | (`IsDeleted`) INCLUDE (`Username`,`Email`,`FirstName`,`LastName`,`RoleId`,`CreatedAt`) | unfiltered list page, covering |
| `IX_Users_LastName_FirstName` | Users | (`LastName`, `FirstName`) | name sorting |
| `IX_AuditLogs_Timestamp` | AuditLogs | (`Timestamp` DESC) | audit list default order |
| `IX_AuditLogs_Entity` | AuditLogs | (`EntityName`, `EntityId`, `Timestamp` DESC) | per-user history |
| `IX_AuditLogs_PerformedByUserId` | AuditLogs | (`PerformedByUserId`, `Timestamp` DESC) | "what did this actor do" |
| `IX_RefreshTokens_UserId` | RefreshTokens | (`UserId`) INCLUDE (`ExpiresAt`,`RevokedAt`) | revoke-all-for-user |
| `IX_RefreshTokens_FamilyId` | RefreshTokens | (`FamilyId`) | revoke-family on reuse detection |

The two covering indexes on `Users` exist because the list query projects a fixed, small column set; covering
it turns the hot page query into a single index range scan with no key lookups.

**Why a filtered unique index plus an unfiltered non-unique one on `Username`.** SQL Server can only use a
filtered index when the query's predicate provably matches the filter. Normal queries carry
`IsDeleted = 0` from the global query filter, so they seek `UQ_Users_ActiveUsername`. Login deliberately
ignores the filter — it must find a soft-deleted row in order to refuse it rather than report "no such
user" — and that lookup has no `IsDeleted` predicate, so it needs the unfiltered index. Two small indexes on
a 50-character column is a cheap price for both paths seeking instead of scanning.

## 3. Query shapes

### Default list page (no filters)

```sql
SELECT u.Id, u.Username, u.Email, u.FirstName, u.LastName, r.Name AS Role,
       u.IsDeleted, u.CreatedAt, u.LastModifiedAt
FROM Users AS u
INNER JOIN Roles AS r ON r.Id = u.RoleId
WHERE u.IsDeleted = 0
ORDER BY u.CreatedAt DESC, u.Id DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
```

`ORDER BY` always carries `Id` as a tiebreaker. Without it, rows sharing a `CreatedAt` value can appear on
two consecutive pages or on none — a classic paging bug that only shows up with real data.

### Search

```sql
WHERE u.IsDeleted = 0
  AND (u.Username  LIKE @term OR u.Email LIKE @term
    OR u.FirstName LIKE @term OR u.LastName LIKE @term)
```

`@term` is `'%value%'`. **Known trade-off:** a leading wildcard cannot seek, so search degrades to an index
scan. That is the right call at this scale (thousands of rows, covering indexes keep it cheap) and the
alternative — a full-text index with `CONTAINS` — is documented as the scaling path in the README's known
limitations. The parameter is still fully parameterised, so this is a performance note, not an injection one.

### Count for pagination metadata

Executed as a second query against the same `IQueryable` before `Skip/Take` — `COUNT(*)` over the filtered
set. Two round trips beat one heavy windowed query for typical page sizes, and EF cannot batch these
safely with `OFFSET/FETCH`.

`003_sample_queries.sql` ships these plus an audit-history query and a "who has Admin" query, each with a
short note on the index it uses.

## 4. Sorting whitelist

Client `sortBy` values map through a dictionary; anything else is a `400`, never interpolated SQL:

| `sortBy` | Expression |
|---|---|
| `username` | `u.Username` |
| `email` | `u.Email` |
| `firstName` | `u.FirstName` |
| `lastName` | `u.LastName` |
| `role` | `r.Name` |
| `createdAt` | `u.CreatedAt` (default, `desc`) |

## 5. Migrations and scripts

```bash
dotnet ef migrations add InitialCreate -p src/UserManagement.Infrastructure -s src/UserManagement.Api
dotnet ef database update -p src/UserManagement.Infrastructure -s src/UserManagement.Api
pwsh database/generate-schema-script.ps1
```

`001_schema.sql` is **generated, never hand-written**, so it cannot drift from the migrations. The generator
adds two things to the raw `dotnet ef migrations script --idempotent` output:

- `-i` / `--idempotent`, so a reviewer can apply it to an existing database safely.
- A `SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;` header. This is not cosmetic: SQL Server **refuses to
  create a filtered index, or to insert into a table that has one, unless both options are ON**. SSMS sets
  them by default and `sqlcmd` does not, so the raw EF output fails partway through for anyone running the
  script from a command line. `002_seed.sql` and `003_sample_queries.sql` carry the same header for the same
  reason. All three scripts were verified end to end against a freshly created database.

## 6. Seeding

**Roles are seeded by the migration**, not by the seeder: they are reference data, so `HasData` puts them in
`001_schema.sql` and in every freshly created database without anyone having to remember a second step.

`DbSeeder` creates only the **demo users**, runs at startup in Development only, and is idempotent
(insert-if-absent by username). Its passwords come from configuration (`Seed:*`); with none configured it
creates nothing and logs why, so a deployed environment cannot acquire well-known accounts by accident.

`002_seed.sql` performs the same three inserts for reviewers who prefer SQL. Its hashes are **precomputed
PBKDF2 values pasted as literals**, produced by the application's own hasher — the same code path that
verifies them at sign-in — so no plaintext password appears in any script. The passwords they correspond to
are documented in the README as development-only.

Demo accounts (credentials documented in the README, clearly marked as development-only):

| Username | Role | Purpose |
|---|---|---|
| `admin` | Admin | full capability demonstration |
| `jdoe` | User | self-profile only; 403 on admin routes |
| `readonly` | ReadOnlyUser | read paths only; 403 on every mutation |

Additionally the seeder inserts ~25 filler users in Development so search, sorting, paging and the role
filter are demonstrable without manual data entry. Filler users share a single known hash and are marked
in `CreatedBy = 'seed'`.

## 7. Connection strings and local options

| Environment | Connection |
|---|---|
| Local (default) | `Server=localhost;Database=UserManagement;Trusted_Connection=True;TrustServerCertificate=True` |
| Local (LocalDB) | `Server=(localdb)\\MSSQLLocalDB;Database=UserManagement;Trusted_Connection=True` |
| Docker Compose | `Server=mssql,1433;Database=UserManagement;User Id=sa;Password=<from env>;TrustServerCertificate=True` |

Real values live in User Secrets or environment variables; `appsettings.example.json` carries placeholders
only.
