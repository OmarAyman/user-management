using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Persistence.Seeding;

/// <summary>
/// Creates the demo accounts. Idempotent: it inserts a user only when that username is absent, so running it
/// repeatedly - on every startup, in every test - changes nothing after the first time.
/// </summary>
/// <remarks>
/// Roles are not seeded here. They are reference data and live in the migration (<c>HasData</c>), so a fresh
/// database and the generated SQL script both contain them without anyone remembering to run this.
/// </remarks>
public sealed class DbSeeder(
    ApplicationDbContext context,
    IPasswordHasher passwordHasher,
    IOptions<SeedOptions> options,
    ILogger<DbSeeder> logger)
{
    private readonly SeedOptions _options = options.Value;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Seeding is disabled; no demo accounts were created");
            return;
        }

        var created = 0;

        created += await EnsureUserAsync(
            "admin",
            "admin@example.com",
            "System",
            "Administrator",
            RoleIds.Admin,
            _options.AdminPassword,
            cancellationToken);

        created += await EnsureUserAsync(
            "jdoe",
            "jane.doe@example.com",
            "Jane",
            "Doe",
            RoleIds.User,
            _options.UserPassword,
            cancellationToken);

        created += await EnsureUserAsync(
            "readonly",
            "read.only@example.com",
            "Read",
            "Only",
            RoleIds.ReadOnlyUser,
            _options.ReadOnlyPassword,
            cancellationToken);

        created += await EnsureSampleUsersAsync(cancellationToken);

        if (created > 0)
        {
            // The seeder runs without an authenticated caller, so the stamping interceptor records
            // SystemActors.System as the actor and the audit trail shows these rows as system-created.
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} user(s)", created);
        }
        else
        {
            logger.LogInformation("Seed data already present; nothing to insert");
        }
    }

    private async Task<int> EnsureUserAsync(
        string username,
        string email,
        string firstName,
        string lastName,
        int roleId,
        string? password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No password configured for demo account {Username}; the account was not created",
                username);
            return 0;
        }

        // IgnoreQueryFilters is not used here on purpose: if a soft-deleted row holds this username, creating a
        // new active one is legitimate under ADR-0009 and the filtered unique index allows it.
        var exists = await context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Username == username, cancellationToken);

        if (exists)
        {
            return 0;
        }

        var user = User.Create(username, email, firstName, lastName, passwordHasher.Hash(password), roleId);
        context.Users.Add(user);

        return 1;
    }

    /// <summary>
    /// Adds filler users spread across the three roles. They share one password so the account list is
    /// realistic without inventing credentials nobody will use.
    /// </summary>
    private async Task<int> EnsureSampleUsersAsync(CancellationToken cancellationToken)
    {
        if (_options.SampleUserCount <= 0 || string.IsNullOrWhiteSpace(_options.UserPassword))
        {
            return 0;
        }

        var existing = await context.Users
            .AsNoTracking()
            .Where(user => user.CreatedBy == SystemActors.Seed)
            .CountAsync(cancellationToken);

        if (existing >= _options.SampleUserCount)
        {
            return 0;
        }

        var firstNames = new[] { "Aisha", "Omar", "Sara", "Youssef", "Layla", "Karim", "Noor", "Tariq" };
        var lastNames = new[] { "Hassan", "Mansour", "Khalil", "Farouk", "Nasser", "Rahman" };
        var roles = new[] { RoleIds.User, RoleIds.ReadOnlyUser, RoleIds.User };
        var hash = passwordHasher.Hash(_options.UserPassword);
        var created = 0;

        for (var index = existing; index < _options.SampleUserCount; index++)
        {
            var firstName = firstNames[index % firstNames.Length];
            var lastName = lastNames[index % lastNames.Length];
            var suffix = (index + 1).ToString(CultureInfo.InvariantCulture);
            var username = $"{firstName[..1].ToLowerInvariant()}{lastName.ToLowerInvariant()}{suffix}";

            var user = User.Create(
                username,
                $"{username}@example.com",
                firstName,
                lastName,
                hash,
                roles[index % roles.Length]);

            // Marks the row as filler so a reviewer can tell demo data from the three documented accounts.
            user.CreatedBy = SystemActors.Seed;

            context.Users.Add(user);
            created++;
        }

        return created;
    }
}
