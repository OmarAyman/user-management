/*
    002_seed.sql - demo accounts for review.

    Roles are NOT seeded here: they are reference data carried by the EF Core migration, so 001_schema.sql
    already contains them. Running this script against a database created by 001_schema.sql is enough.

    NO PLAINTEXT PASSWORD APPEARS IN THIS FILE. The values below are PBKDF2-HMAC-SHA512 hashes in the
    ASP.NET Core v3 format (0x01 version marker, 128-bit salt, 210,000 iterations), produced by the
    application's own hasher - the same code path that verifies them at sign-in.

    The passwords they correspond to are documented in README.md. They are development-only demonstration
    credentials and must never be used in a deployed environment.

    Idempotent: each insert is guarded, so running the script twice changes nothing.

    The SET options below are required, not cosmetic: the Users table carries filtered unique indexes, and
    SQL Server refuses any INSERT into such a table unless QUOTED_IDENTIFIER and ANSI_NULLS are ON. SSMS sets
    both by default; sqlcmd does not, so without this header the script fails on its first insert.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;

/* --------------------------------------------------------------------------------------------------
   Administrator - full capability.
   -------------------------------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM [Users] WHERE [Username] = N'admin')
BEGIN
    INSERT INTO [Users]
        ([Id], [Username], [Email], [FirstName], [LastName], [PasswordHash], [SecurityStamp],
         [RoleId], [IsDeleted], [FailedLoginAttempts], [CreatedAt], [CreatedBy])
    VALUES
        (NEWID(), N'admin', N'admin@example.com', N'System', N'Administrator',
         N'AQAAAAIAAYagAAAAEB30ff7RCU6GsfRbofzWEtriRCa3bE7LBqAsp0V5h++GrpCw0VY7hXGnqhRAlAZyDA==',
         NEWID(), 1, 0, 0, SYSDATETIMEOFFSET(), N'seed');
END;
GO

/* --------------------------------------------------------------------------------------------------
   Standard user - may read the user list and edit only their own profile.
   -------------------------------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM [Users] WHERE [Username] = N'jdoe')
BEGIN
    INSERT INTO [Users]
        ([Id], [Username], [Email], [FirstName], [LastName], [PasswordHash], [SecurityStamp],
         [RoleId], [IsDeleted], [FailedLoginAttempts], [CreatedAt], [CreatedBy])
    VALUES
        (NEWID(), N'jdoe', N'jane.doe@example.com', N'Jane', N'Doe',
         N'AQAAAAIAAYagAAAAEA1FxvdMzseHhVdmD8MgCTeW6AE1SJslEX0DWioGjpb7msZftfAsgK0KQxpj0FbddQ==',
         NEWID(), 2, 0, 0, SYSDATETIMEOFFSET(), N'seed');
END;
GO

/* --------------------------------------------------------------------------------------------------
   Read-only user - may read the user list and edit their own profile, and is refused every other write.
   ("Read-only" refers to other people's data; the authorization matrix grants self-profile editing to
   all three roles.)
   -------------------------------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM [Users] WHERE [Username] = N'readonly')
BEGIN
    INSERT INTO [Users]
        ([Id], [Username], [Email], [FirstName], [LastName], [PasswordHash], [SecurityStamp],
         [RoleId], [IsDeleted], [FailedLoginAttempts], [CreatedAt], [CreatedBy])
    VALUES
        (NEWID(), N'readonly', N'read.only@example.com', N'Read', N'Only',
         N'AQAAAAIAAYagAAAAEHSTHLLU85RA9zrKRcnEfxoAG6eUcxJSsWaS/PytRSVLFNZl3HUsXmvuCHnmEJ6oCA==',
         NEWID(), 3, 0, 0, SYSDATETIMEOFFSET(), N'seed');
END;
GO

COMMIT TRANSACTION;
GO

/*
    Note on the audit trail: rows inserted by this script produce no audit entries, because auditing is an
    application concern implemented as an EF Core interceptor. Accounts created through the API - or by the
    application's own seeder - are audited. That difference is deliberate and worth knowing when reading the
    trail of a database that was bootstrapped with SQL.
*/

SELECT u.[Username], r.[Name] AS [Role], u.[CreatedBy], u.[CreatedAt]
FROM [Users] u
JOIN [Roles] r ON r.[Id] = u.[RoleId]
WHERE u.[Username] IN (N'admin', N'jdoe', N'readonly')
ORDER BY u.[Username];
GO
