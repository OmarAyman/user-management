using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Infrastructure.Configuration;
using UserManagement.Infrastructure.Persistence;
using UserManagement.Infrastructure.Persistence.Interceptors;
using UserManagement.Infrastructure.Persistence.Repositories;
using UserManagement.Infrastructure.Persistence.Seeding;
using UserManagement.Infrastructure.Security;
using UserManagement.Infrastructure.Time;

namespace UserManagement.Infrastructure;

/// <summary>
/// Composition for the Infrastructure layer: the database context, its interceptors, the repositories and the
/// security primitives. This is the only place that names a concrete implementation of an Application port.
/// </summary>
public static class DependencyInjection
{
    public const string DefaultConnectionName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            // Fail at startup, not at the first sign-in attempt: a key too short to sign with is a
            // configuration error, and the only useful moment to discover it is boot.
            .Validate(
                options => options.Key.Length >= JwtOptions.MinimumKeyBytes,
                $"Jwt:Key must be at least {JwtOptions.MinimumKeyBytes} characters.")
            .ValidateOnStart();

        services.AddOptions<RefreshTokenOptions>()
            .Bind(configuration.GetSection(RefreshTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Scoped: both interceptors need the current caller and the request clock.
        services.AddScoped<AuditableEntitySaveChangesInterceptor>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<ApplicationDbContext>((serviceProvider, optionsBuilder) =>
        {
            optionsBuilder.UseSqlServer(
                configuration.GetConnectionString(DefaultConnectionName),
                sqlServer => sqlServer
                    .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)
                    // Transient network faults are worth one automatic retry; anything persistent should
                    // surface as an error rather than be hidden behind a long retry loop.
                    .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null));

            optionsBuilder.AddInterceptors(
                serviceProvider.GetRequiredService<AuditableEntitySaveChangesInterceptor>(),
                serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());

            // EF warns that RefreshToken requires a User that the soft-delete filter can hide. That is a real
            // hazard in general, and here it is handled deliberately rather than suppressed blindly:
            // RefreshTokenRepository never Includes the navigation - it loads the owner through the repository's
            // sanctioned opt-out - precisely so a token belonging to a removed account is visible and can be
            // refused, instead of arriving with a null User that looks like an unknown token.
            optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(
                CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        });

        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Singleton: holds no state beyond the hasher instance and the dummy hash it verifies against.
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.AddScoped<IAccessTokenIssuer, AccessTokenIssuer>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        services.AddScoped<DbSeeder>();

        return services;
    }
}
