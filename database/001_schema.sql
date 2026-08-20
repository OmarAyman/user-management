/*
    001_schema.sql - complete schema for the User Management module.

    GENERATED FILE - do not edit by hand. Produced from the EF Core migrations by
    database/generate-schema-script.ps1, so this script and the migrations cannot drift apart.

    Idempotent: safe to run against an empty database or an existing one.

    Includes the three seeded roles (Admin, User, ReadOnlyUser), which are carried by the migration as
    reference data. Demo user accounts are in 002_seed.sql.

    The SET options below are required, not cosmetic: the schema contains filtered unique indexes, and
    SQL Server refuses to create one unless QUOTED_IDENTIFIER and ANSI_NULLS are ON. SSMS sets them by
    default; sqlcmd does not.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [EntityName] nvarchar(100) NOT NULL,
        [EntityId] nvarchar(64) NOT NULL,
        [EntityDisplayName] nvarchar(100) NULL,
        [Action] tinyint NOT NULL,
        [PerformedByUserId] uniqueidentifier NULL,
        [PerformedByUsername] nvarchar(50) NOT NULL,
        [Timestamp] datetimeoffset NOT NULL,
        [IpAddress] nvarchar(45) NOT NULL,
        [OldValues] nvarchar(max) NULL,
        [NewValues] nvarchar(max) NULL,
        [CorrelationId] nvarchar(64) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [CK_AuditLogs_Action] CHECK ([Action] BETWEEN 0 AND 4)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] int NOT NULL,
        [Name] nvarchar(50) NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [Username] nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        [Email] nvarchar(256) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [SecurityStamp] uniqueidentifier NOT NULL,
        [RoleId] int NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [DeletedBy] nvarchar(50) NULL,
        [FailedLoginAttempts] int NOT NULL DEFAULT 0,
        [LockoutEndAt] datetimeoffset NULL,
        [LastLoginAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(50) NOT NULL,
        [LastModifiedAt] datetimeoffset NULL,
        [LastModifiedBy] nvarchar(50) NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY NONCLUSTERED ([Id]),
        CONSTRAINT [CK_Users_DeletedConsistency] CHECK (([IsDeleted] = 0 AND [DeletedAt] IS NULL) OR ([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL)),
        CONSTRAINT [FK_Users_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [FamilyId] uniqueidentifier NOT NULL,
        [TokenHash] char(64) NOT NULL,
        [ReplacedByTokenId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedByIp] nvarchar(45) NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        [RevokedByIp] nvarchar(45) NULL,
        [RevocationReason] tinyint NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RefreshTokens_RevocationReason] CHECK ([RevocationReason] BETWEEN 0 AND 6),
        CONSTRAINT [FK_RefreshTokens_RefreshTokens_ReplacedByTokenId] FOREIGN KEY ([ReplacedByTokenId]) REFERENCES [RefreshTokens] ([Id]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] ON;
    EXEC(N'INSERT INTO [Roles] ([Id], [Name])
    VALUES (1, N''Admin''),
    (2, N''User''),
    (3, N''ReadOnlyUser'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Entity] ON [AuditLogs] ([EntityName], [EntityId], [Timestamp] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_PerformedByUserId] ON [AuditLogs] ([PerformedByUserId], [Timestamp] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Timestamp] ON [AuditLogs] ([Timestamp] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_FamilyId] ON [RefreshTokens] ([FamilyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_ReplacedByTokenId] ON [RefreshTokens] ([ReplacedByTokenId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]) INCLUDE ([ExpiresAt], [RevokedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Roles_Name] ON [Roles] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE CLUSTERED INDEX [CIX_Users_CreatedAt] ON [Users] ([CreatedAt], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_IsDeleted] ON [Users] ([IsDeleted]) INCLUDE ([Username], [Email], [FirstName], [LastName], [RoleId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_LastName_FirstName] ON [Users] ([LastName], [FirstName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_RoleId_IsDeleted] ON [Users] ([RoleId], [IsDeleted]) INCLUDE ([Username], [Email], [FirstName], [LastName], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_Username_All] ON [Users] ([Username]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UQ_Users_ActiveEmail] ON [Users] ([Email]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UQ_Users_ActiveUsername] ON [Users] ([Username]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820081616_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260820081616_InitialCreate', N'10.0.11');
END;

COMMIT;
GO


