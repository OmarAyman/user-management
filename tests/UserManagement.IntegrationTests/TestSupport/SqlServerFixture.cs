using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Infrastructure;
using UserManagement.Infrastructure.Persistence;

namespace UserManagement.IntegrationTests.TestSupport;

/// <summary>
/// One real SQL Server for the whole test run, with the schema created by the actual migrations.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory provider is not an option here: global query filters, filtered unique indexes,
/// <c>rowversion</c> concurrency, collation-driven case-insensitivity and <c>LIKE</c> translation are precisely
/// what these tests exist to prove, and the in-memory provider models none of them faithfully.
/// </para>
/// <para>
/// Testcontainers is the default. Setting <c>USERMANAGEMENT_TEST_SQL</c> to a connection string uses that
/// server instead - the documented fallback for a machine without Docker.
/// </para>
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    private ServiceProvider? _services;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>The actor recorded by the stamping and audit interceptors. Settable per test.</summary>
    public TestCurrentUserService CurrentUser { get; } = new();

    public TestClientInfoProvider ClientInfo { get; } = new();

    public async Task InitializeAsync()
    {
        // One database per fixture, because both collections initialise in parallel. See TestDatabase.
        var fallback = TestDatabase.ConnectionStringFor("Persistence");

        if (fallback is not null)
        {
            ConnectionString = fallback;
        }
        else
        {
            // The image is passed to the constructor: Testcontainers 4.x deprecated the parameterless one so
            // that a test run cannot silently depend on whatever tag the library defaults to this month.
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        _services = BuildServices(ConnectionString);

        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The schema under test is the one the migrations produce, not one EnsureCreated invented.
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>A fresh scope, and therefore a fresh change tracker - as a real request would have.</summary>
    public AsyncServiceScope CreateScope() =>
        (_services ?? throw new InvalidOperationException("Fixture not initialised")).CreateAsyncScope();

    private ServiceProvider BuildServices(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.DefaultConnectionName}"] = connectionString,
                ["Jwt:Issuer"] = "usermanagement.tests",
                ["Jwt:Audience"] = "usermanagement.tests",
                ["Jwt:Key"] = "integration-tests-signing-key-not-a-real-secret-0123456789",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["RefreshToken:LifetimeDays"] = "7",
                ["RefreshToken:CookieName"] = "refreshToken",
                ["RefreshToken:CookiePath"] = "/api/auth",
                ["Seed:Enabled"] = "false",
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        // The real composition root, so these tests exercise the same registrations the API uses.
        services.AddInfrastructure(configuration);

        // The two ports the API layer normally implements over HttpContext.
        services.AddSingleton<ICurrentUserService>(CurrentUser);
        services.AddSingleton<IClientInfoProvider>(ClientInfo);

        return services.BuildServiceProvider();
    }
}

/// <summary>Shares one container across every collection member, so the image starts once per run.</summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sqlserver";
}
