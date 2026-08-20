/*
    003_sample_queries.sql - the queries the application actually issues, with the index each one uses.

    Written so a reviewer can check the access paths without reading C#, and so the intent behind each index
    in 001_schema.sql is visible. Run with "Include Actual Execution Plan" on to confirm the seeks.

    QUOTED_IDENTIFIER is set for the same reason as in the other two scripts: the tables carry filtered
    indexes, and sqlcmd does not turn it on by default.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

/* ==================================================================================================
   1. Default user list page - newest first.
      Index: CIX_Users_CreatedAt (clustered) range scan, no key lookups.

      ORDER BY carries Id as a tiebreaker on purpose. Without it, rows sharing a CreatedAt value can appear
      on two consecutive pages or on none - a paging bug that only shows up once real data has duplicate
      timestamps.
   ================================================================================================== */
DECLARE @PageNumber int = 1;
DECLARE @PageSize   int = 10;

SELECT u.[Id], u.[Username], u.[Email], u.[FirstName], u.[LastName], r.[Name] AS [Role],
       u.[IsDeleted], u.[CreatedAt], u.[LastModifiedAt]
FROM [Users] u
JOIN [Roles] r ON r.[Id] = u.[RoleId]
WHERE u.[IsDeleted] = 0
ORDER BY u.[CreatedAt] DESC, u.[Id] DESC
OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
GO

/* ==================================================================================================
   2. Total count for the paging metadata.
      Index: IX_Users_IsDeleted, covered.

      Issued as a second query rather than a window function: for typical page sizes two cheap round trips
      beat one heavier query, and EF cannot batch a windowed count with OFFSET/FETCH safely.
   ================================================================================================== */
SELECT COUNT_BIG(*) AS [TotalCount]
FROM [Users]
WHERE [IsDeleted] = 0;
GO

/* ==================================================================================================
   3. Search across the four searchable columns.
      Index: IX_Users_IsDeleted (covering scan).

      Known trade-off: a leading wildcard cannot seek, so this degrades to a scan. At this scale the
      covering index keeps it cheap; the scaling path is a full-text index with CONTAINS, which is recorded
      as a known limitation rather than pretended away. The parameter is still fully parameterised - this is
      a performance note, not an injection one.
   ================================================================================================== */
DECLARE @Search nvarchar(100) = N'%doe%';

SELECT u.[Id], u.[Username], u.[Email], u.[FirstName], u.[LastName], r.[Name] AS [Role]
FROM [Users] u
JOIN [Roles] r ON r.[Id] = u.[RoleId]
WHERE u.[IsDeleted] = 0
  AND (u.[Username]  LIKE @Search
    OR u.[Email]     LIKE @Search
    OR u.[FirstName] LIKE @Search
    OR u.[LastName]  LIKE @Search)
ORDER BY u.[CreatedAt] DESC, u.[Id] DESC
OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY;
GO

/* ==================================================================================================
   4. Role filter.
      Index: IX_Users_RoleId_IsDeleted - a seek, with every projected column included.
   ================================================================================================== */
DECLARE @RoleId int = 2;

SELECT u.[Id], u.[Username], u.[Email], u.[FirstName], u.[LastName], u.[CreatedAt]
FROM [Users] u
WHERE u.[RoleId] = @RoleId AND u.[IsDeleted] = 0
ORDER BY u.[CreatedAt] DESC, u.[Id] DESC;
GO

/* ==================================================================================================
   5. Sign-in lookup.
      Index: IX_Users_Username_All - unfiltered on purpose.

      Sign-in must find a soft-deleted row in order to refuse it, rather than report "no such user", so this
      query carries no IsDeleted predicate. That is exactly why the filtered unique index cannot serve it and
      a second, unfiltered index on Username exists.
   ================================================================================================== */
DECLARE @Username nvarchar(50) = N'admin';

SELECT u.[Id], u.[Username], u.[PasswordHash], u.[IsDeleted], u.[LockoutEndAt],
       u.[FailedLoginAttempts], r.[Name] AS [Role]
FROM [Users] u
JOIN [Roles] r ON r.[Id] = u.[RoleId]
WHERE u.[Username] = @Username;
GO

/* ==================================================================================================
   6. Uniqueness check for a new or edited user - active rows only.
      Index: UQ_Users_ActiveUsername / UQ_Users_ActiveEmail (filtered, unique).

      Returns nothing for a name held only by a soft-deleted user: identifiers are released on deletion
      (ADR-0009). The filtered unique index, not this query, is the authority under a race.
   ================================================================================================== */
DECLARE @CandidateUsername nvarchar(50) = N'admin';

SELECT CAST(CASE WHEN EXISTS (
           SELECT 1 FROM [Users] WHERE [Username] = @CandidateUsername AND [IsDeleted] = 0
       ) THEN 1 ELSE 0 END AS bit) AS [UsernameTaken];
GO

/* ==================================================================================================
   7. Administrators still active - the last-Admin guard (BR-02, BR-03).
      Index: IX_Users_RoleId_IsDeleted seek.

      The application runs this inside the same transaction as the delete or role change, so two concurrent
      demotions cannot race the system down to zero administrators.
   ================================================================================================== */
SELECT COUNT(*) AS [ActiveAdmins]
FROM [Users]
WHERE [RoleId] = 1 AND [IsDeleted] = 0;
GO

/* ==================================================================================================
   8. Deleted users - the Admin-only listing.
      Index: IX_Users_IsDeleted seek.

      Reachable only through GET /api/users/deleted behind an Admin policy: in the application, suspending
      the soft-delete filter is an authorization decision, never a query parameter (ADR-0004).
   ================================================================================================== */
SELECT u.[Id], u.[Username], u.[Email], u.[DeletedAt], u.[DeletedBy]
FROM [Users] u
WHERE u.[IsDeleted] = 1
ORDER BY u.[DeletedAt] DESC;
GO

/* ==================================================================================================
   9. Audit history for one user.
      Index: IX_AuditLogs_Entity (EntityName, EntityId, Timestamp DESC) seek.

      Filtered by EntityId - the immutable user id - never by username. That is what keeps history
      unambiguous after a deleted user's username has been reused by somebody else.
   ================================================================================================== */
DECLARE @UserId nvarchar(64) = (SELECT CAST([Id] AS nvarchar(64)) FROM [Users] WHERE [Username] = N'admin');

SELECT a.[Id], a.[Action], a.[EntityDisplayName], a.[PerformedByUsername], a.[Timestamp],
       a.[IpAddress], a.[OldValues], a.[NewValues]
FROM [AuditLogs] a
WHERE a.[EntityName] = N'User' AND a.[EntityId] = @UserId
ORDER BY a.[Timestamp] DESC;
GO

/* ==================================================================================================
   10. Every privilege change in the system.
       Index: IX_AuditLogs_Timestamp.

       Action 4 is RoleChange. A dedicated row is written for a role change in addition to the general
       update row, precisely so this question is a single-column filter instead of a JSON diff.
   ================================================================================================== */
SELECT a.[Timestamp], a.[EntityDisplayName] AS [TargetUser], a.[PerformedByUsername] AS [Actor],
       a.[OldValues], a.[NewValues], a.[IpAddress]
FROM [AuditLogs] a
WHERE a.[Action] = 4
ORDER BY a.[Timestamp] DESC;
GO

/* ==================================================================================================
   11. Refresh-token families for one user - session forensics.
       Index: IX_RefreshTokens_UserId, then IX_RefreshTokens_FamilyId.

       RevocationReason 1 is ReuseDetected: a whole family revoked with that reason means a rotated token
       was presented twice, which is treated as theft.
   ================================================================================================== */
SELECT t.[FamilyId], t.[CreatedAt], t.[ExpiresAt], t.[RevokedAt], t.[RevocationReason],
       t.[ReplacedByTokenId], t.[CreatedByIp]
FROM [RefreshTokens] t
JOIN [Users] u ON u.[Id] = t.[UserId]
WHERE u.[Username] = N'admin'
ORDER BY t.[FamilyId], t.[CreatedAt];
GO

/* ==================================================================================================
   12. Confirmation that no password material sits in the audit trail.
       Expected result: 0 rows. Asserted by an automated test as well; this is the version a reviewer can
       run by hand.
   ================================================================================================== */
SELECT COUNT(*) AS [RowsContainingHashMaterial]
FROM [AuditLogs]
WHERE ISNULL([OldValues], N'') LIKE N'%AQAAAA%'
   OR ISNULL([NewValues], N'') LIKE N'%AQAAAA%';
GO
