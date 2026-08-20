<#
    Regenerates database/001_schema.sql from the EF Core migrations.

    Run this after adding a migration, so the SQL script and the migrations can never disagree - the script is
    generated from the same model, never hand-written.

        pwsh database/generate-schema-script.ps1

    Why the header this script prepends matters: the schema contains FILTERED indexes
    (UQ_Users_ActiveUsername, UQ_Users_ActiveEmail), and SQL Server refuses to create a filtered index unless
    QUOTED_IDENTIFIER and ANSI_NULLS are ON. SSMS sets both by default; sqlcmd does NOT, so the raw output of
    "dotnet ef migrations script" fails partway through when a reviewer runs it with sqlcmd. Prepending the SET
    statements makes the script work with either tool.
#>

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $PSScriptRoot '001_schema.sql'
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) "usermanagement-schema-$([guid]::NewGuid()).sql"

Write-Host 'Generating an idempotent schema script from the migrations...'

# -i produces an idempotent script, so it can be applied to an existing database safely.
dotnet ef migrations script `
    --idempotent `
    --output $temporary `
    --project (Join-Path $repositoryRoot 'src/UserManagement.Infrastructure') `
    --startup-project (Join-Path $repositoryRoot 'src/UserManagement.Api')

if ($LASTEXITCODE -ne 0) { throw 'dotnet ef migrations script failed.' }

$header = @'
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

'@

$body = Get-Content -Path $temporary -Raw
Set-Content -Path $output -Value ($header + $body) -Encoding UTF8
Remove-Item -Path $temporary -Force

Write-Host "Wrote $output"
