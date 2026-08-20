using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Domain.Constants;
using UserManagement.Infrastructure.Persistence;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Persistence;

/// <summary>
/// Asserts the shape of the schema the migrations actually produce.
/// </summary>
/// <remarks>
/// This exists because of a defect found during Phase 2: declaring two indexes over the same column without
/// explicit names made EF treat the second declaration as a reconfiguration of the first, so the filtered
/// unique index was silently renamed and the unfiltered login index was never created. The model looked
/// correct and only the emitted DDL was wrong - which is exactly the class of mistake a behavioural test can
/// miss and a metadata test cannot.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public sealed class SchemaTests(SqlServerFixture fixture)
{
    [Theory]
    [InlineData("UQ_Users_ActiveUsername", true, "([IsDeleted]=(0))")]
    [InlineData("UQ_Users_ActiveEmail", true, "([IsDeleted]=(0))")]
    [InlineData("IX_Users_Username_All", false, null)]
    public async Task The_expected_user_indexes_exist_with_the_right_uniqueness_and_filter(
        string indexName,
        bool expectedUnique,
        string? expectedFilter)
    {
        var indexes = await QueryIndexesAsync("Users");

        var index = Assert.Single(indexes, candidate => candidate.Name == indexName);

        Assert.Equal(expectedUnique, index.IsUnique);
        Assert.Equal(expectedFilter, index.Filter);
    }

    [Fact]
    public async Task The_clustered_index_is_on_created_at_and_the_key_is_not_clustered()
    {
        var indexes = await QueryIndexesAsync("Users");

        // Newest-first paging is the hot path, so that is what the clustered index serves.
        Assert.Equal("CLUSTERED", Assert.Single(indexes, index => index.Name == "CIX_Users_CreatedAt").Type);
        Assert.Equal("NONCLUSTERED", Assert.Single(indexes, index => index.Name == "PK_Users").Type);
    }

    [Fact]
    public async Task The_audit_table_is_clustered_on_its_identity_key()
    {
        var indexes = await QueryIndexesAsync("AuditLogs");

        // Append-only table: an ascending clustered key means no page splits.
        Assert.Equal("CLUSTERED", Assert.Single(indexes, index => index.Name == "PK_AuditLogs").Type);
    }

    [Fact]
    public async Task The_row_version_column_is_a_real_rowversion()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var typeName = await context.Database
            .SqlQuery<string>($@"
                SELECT t.name AS Value
                FROM sys.columns c
                JOIN sys.types t ON t.user_type_id = c.user_type_id
                WHERE c.object_id = OBJECT_ID('Users') AND c.name = 'RowVersion'")
            .SingleAsync();

        Assert.Equal("timestamp", typeName);
    }

    [Fact]
    public async Task The_seeded_roles_are_present_with_their_fixed_ids()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var roles = await context.Roles.AsNoTracking().OrderBy(role => role.Id).ToListAsync();

        // Seeded by the migration rather than the seeder, so a fresh database has them without any extra step.
        Assert.Collection(
            roles,
            role => AssertRole(role, RoleIds.Admin, RoleNames.Admin),
            role => AssertRole(role, RoleIds.User, RoleNames.User),
            role => AssertRole(role, RoleIds.ReadOnlyUser, RoleNames.ReadOnlyUser));
    }

    [Fact]
    public async Task Every_expected_table_exists()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tables = await context.Database
            .SqlQuery<string>($"SELECT name AS Value FROM sys.tables ORDER BY name")
            .ToListAsync();

        Assert.Contains("Users", tables);
        Assert.Contains("Roles", tables);
        Assert.Contains("AuditLogs", tables);
        Assert.Contains("RefreshTokens", tables);
    }

    private static void AssertRole(Domain.Entities.Role role, int expectedId, string expectedName)
    {
        Assert.Equal(expectedId, role.Id);
        Assert.Equal(expectedName, role.Name);
    }

    private async Task<List<IndexInfo>> QueryIndexesAsync(string table)
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.Database
            .SqlQuery<IndexInfo>($@"
                SELECT i.name AS Name,
                       i.is_unique AS IsUnique,
                       i.type_desc AS Type,
                       i.filter_definition AS Filter
                FROM sys.indexes i
                WHERE i.object_id = OBJECT_ID({table}) AND i.name IS NOT NULL")
            .ToListAsync();
    }

    private sealed record IndexInfo(string Name, bool IsUnique, string Type, string? Filter);
}
