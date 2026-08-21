using Microsoft.Data.SqlClient;

namespace UserManagement.IntegrationTests.TestSupport;

/// <summary>
/// Resolves the connection string for the <c>USERMANAGEMENT_TEST_SQL</c> fallback, one database per fixture.
/// </summary>
/// <remarks>
/// <para>
/// There are two collection fixtures - <see cref="ApiFixture"/> for the HTTP stack and
/// <see cref="SqlServerFixture"/> for the persistence layer - and xUnit runs their collections in parallel.
/// On the Testcontainers path that is harmless, because each fixture starts its own container and therefore
/// owns its own database.
/// </para>
/// <para>
/// The fallback had no such separation: both fixtures read the same connection string, so both migrated the
/// same database at the same time and then interleaved writes across the same tables. The symptom was not a
/// tidy assertion failure but 122 of 153 tests dying with "A severe error occurred on the current command" -
/// which reads like a broken SQL Server rather than two test hosts fighting over one schema. It surfaced the
/// first time the suite was run inside a container, where the fallback is the only sensible option, because
/// mounting the Docker socket just to let Testcontainers start siblings is a worse trade.
/// </para>
/// <para>
/// So the fallback gives each fixture its own database, derived from whatever name was configured. EF creates
/// them on first migration, and they persist between runs exactly as a manually specified test database
/// would.
/// </para>
/// </remarks>
internal static class TestDatabase
{
    private const string FallbackEnvironmentVariable = "USERMANAGEMENT_TEST_SQL";

    private const string DefaultDatabaseName = "UserManagementTests";

    /// <summary>
    /// The configured fallback connection string with a fixture-specific database, or null when none is set -
    /// in which case the caller starts a container instead.
    /// </summary>
    /// <param name="purpose">Short fixture name, appended to the database name. Must be unique per fixture.</param>
    internal static string? ConnectionStringFor(string purpose)
    {
        var configured = Environment.GetEnvironmentVariable(FallbackEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var builder = new SqlConnectionStringBuilder(configured);

        var baseName = string.IsNullOrWhiteSpace(builder.InitialCatalog)
            ? DefaultDatabaseName
            : builder.InitialCatalog;

        builder.InitialCatalog = $"{baseName}_{purpose}";

        return builder.ConnectionString;
    }
}
