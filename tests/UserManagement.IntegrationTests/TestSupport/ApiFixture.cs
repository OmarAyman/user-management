using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using UserManagement.Infrastructure;

namespace UserManagement.IntegrationTests.TestSupport;

/// <summary>
/// The whole API in-process, over a real SQL Server, with the schema built by the migrations and the demo
/// accounts created by the application's own seeder.
/// </summary>
/// <remarks>
/// Requests go through the actual pipeline - authentication, authorization, the validation filter, the
/// exception handlers, the rate limiter - so these tests exercise what a client experiences rather than a
/// handler in isolation. Handlers already have unit tests; this layer exists to catch the wiring.
/// </remarks>
public sealed class ApiFixture : IAsyncLifetime
{
    private const string FallbackEnvironmentVariable = "USERMANAGEMENT_TEST_SQL";

    private MsSqlContainer? _container;
    private UserManagementApi? _api;

    public string ConnectionString { get; private set; } = string.Empty;

    public UserManagementApi Api => _api ?? throw new InvalidOperationException("Fixture not initialised.");

    public async Task InitializeAsync()
    {
        var fallback = Environment.GetEnvironmentVariable(FallbackEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            ConnectionString = fallback;
        }
        else
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        _api = CreateApi();

        // Forces host construction, which is what applies migrations and seeds the demo accounts.
        using var client = _api.CreateClient();
        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        AssertHostUsesTestDatabase();
    }

    /// <summary>
    /// Proves the host really connected to the test database.
    /// </summary>
    /// <remarks>
    /// Added after a configuration-override mistake let the suite run against the developer's local SQL Server
    /// while every test still passed. A test that silently targets the wrong database is worse than a failing
    /// one, so this fails loudly instead.
    /// </remarks>
    private void AssertHostUsesTestDatabase()
    {
        using var scope = Api.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<Infrastructure.Persistence.ApplicationDbContext>();

        var actual = context.Database.GetConnectionString() ?? string.Empty;
        var expectedDatabase = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString)
            .InitialCatalog;
        var actualDatabase = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(actual).InitialCatalog;

        if (!string.Equals(expectedDatabase, actualDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The test host is pointed at database '{actualDatabase}' instead of the test database "
                + $"'{expectedDatabase}'. Configuration overrides are not reaching the host.");
        }
    }

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// A second host over the same database, with configuration overrides. Used by the rate-limit test, which
    /// needs a tiny permit limit that would otherwise make every other test flaky.
    /// </summary>
    public UserManagementApi CreateApi(IDictionary<string, string?>? overrides = null) =>
        new(ConnectionString, overrides);
}

/// <summary>The test host. Configuration is supplied in memory, so no test depends on a file on disk.</summary>
public sealed class UserManagementApi(string connectionString, IDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development, because that is the only environment where HTTPS redirection is off - a test client
        // speaking http to a redirecting host would see 307s instead of the responses under test.
        builder.UseEnvironment("Development");

        // UseSetting, not ConfigureAppConfiguration. Program reads several values directly off
        // builder.Configuration while composing services - the rate limits, the JWT parameters, the CORS
        // origins - and a source added through ConfigureAppConfiguration is applied too late to affect those
        // reads. The symptom is quiet and expensive: the host silently keeps the developer's own
        // appsettings.Development.json, so the suite runs against the local database and the production rate
        // limit. UseSetting lands in host configuration, which is present before any of that code runs.
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"ConnectionStrings:{DependencyInjection.DefaultConnectionName}"] = connectionString,
                ["Jwt:Issuer"] = "usermanagement.tests",
                ["Jwt:Audience"] = "usermanagement.tests",
                ["Jwt:Key"] = "integration-tests-signing-key-not-a-real-secret-0123456789",
                ["Jwt:AccessTokenMinutes"] = "15",

                // The test client speaks http, so a Secure cookie would never be sent back.
                ["RefreshToken:SecureCookie"] = "false",

                ["Database:MigrateOnStartup"] = "true",
                ["Seed:Enabled"] = "true",
                ["Seed:AdminPassword"] = DemoCredentials.AdminPassword,
                ["Seed:UserPassword"] = DemoCredentials.UserPassword,
                ["Seed:ReadOnlyPassword"] = DemoCredentials.ReadOnlyPassword,
                ["Seed:SampleUserCount"] = "12",

                // High enough that the auth tests cannot trip it; the limiter itself is proved by a test that
                // overrides this to a tiny value.
                ["RateLimiting:AuthPermitLimit"] = "1000",
                ["RateLimiting:AuthWindowSeconds"] = "60",
            };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }

    /// <summary>A client that keeps cookies, so the refresh-token flow behaves as it does in a browser.</summary>
    public HttpClient CreateCookieClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    public IServiceScope CreateScope() => Services.CreateScope();
}
