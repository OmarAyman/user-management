using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Infrastructure.Persistence;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Persistence;

/// <summary>
/// Proves ADR-0009: username and email are unique among <em>active</em> users only, enforced by filtered unique
/// indexes, with audit identity resting on the immutable user id instead of the name.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class UniquenessTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Two_active_users_cannot_share_a_username()
    {
        var username = TestData.Username("dupe");

        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Users.Add(TestData.NewUser(username));
        await context.SaveChangesAsync();

        // A distinct email, so the only thing that collides is the username and the assertion can name the
        // index that rejected the row.
        context.Users.Add(TestData.NewUser(username, email: $"{TestData.Username("other")}@example.com"));

        // The filtered unique index is the real guard. Handler pre-checks exist to turn this into a clean 409,
        // but the database is what makes the rule true under a race.
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var sqlException = Assert.IsType<SqlException>(exception.InnerException);
        Assert.Contains("UQ_Users_ActiveUsername", sqlException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_active_users_cannot_share_an_email()
    {
        var email = $"{TestData.Username("sharedemail")}@example.com";

        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Users.Add(TestData.NewUser(TestData.Username("first"), email: email));
        await context.SaveChangesAsync();

        context.Users.Add(TestData.NewUser(TestData.Username("second"), email: email));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var sqlException = Assert.IsType<SqlException>(exception.InnerException);
        Assert.Contains("UQ_Users_ActiveEmail", sqlException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Username_comparison_is_case_insensitive()
    {
        var username = TestData.Username("case");

        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Users.Add(TestData.NewUser(
            username.ToLowerInvariant(),
            email: $"{TestData.Username("lower")}@example.com"));
        await context.SaveChangesAsync();

        context.Users.Add(TestData.NewUser(
            username.ToUpperInvariant(),
            email: $"{TestData.Username("upper")}@example.com"));

        // Explicit CI collation on the column, so uniqueness does not depend on the server's default.
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var sqlException = Assert.IsType<SqlException>(exception.InnerException);
        Assert.Contains("UQ_Users_ActiveUsername", sqlException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_soft_deleted_username_can_be_taken_by_a_new_user()
    {
        var username = TestData.Username("reuse");
        Guid originalId;

        await using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var original = TestData.NewUser(username);
            context.Users.Add(original);
            await context.SaveChangesAsync();
            originalId = original.Id;

            original.SoftDelete("admin", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        await using var reuseScope = fixture.CreateScope();
        var reuseContext = reuseScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var replacement = TestData.NewUser(username);
        reuseContext.Users.Add(replacement);

        // This is the behaviour the original ADR-0009 forbade and the review reversed: deleting a user releases
        // their identifiers instead of consuming them permanently.
        await reuseContext.SaveChangesAsync();

        Assert.NotEqual(originalId, replacement.Id);

        // And the audit trail stays unambiguous, because every row names the id rather than the name.
        var rowsForOriginal = await reuseContext.AuditLogs
            .Where(log => log.EntityId == originalId.ToString())
            .ToListAsync();

        var rowsForReplacement = await reuseContext.AuditLogs
            .Where(log => log.EntityId == replacement.Id.ToString())
            .ToListAsync();

        Assert.NotEmpty(rowsForOriginal);
        Assert.NotEmpty(rowsForReplacement);
        Assert.All(rowsForOriginal, log => Assert.Equal(username, log.EntityDisplayName));
        Assert.All(rowsForReplacement, log => Assert.Equal(username, log.EntityDisplayName));
    }

    [Fact]
    public async Task Uniqueness_checks_ignore_soft_deleted_rows()
    {
        var username = TestData.Username("takencheck");

        await using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = TestData.NewUser(username);
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            Assert.True(await users.IsUsernameTakenAsync(username, null, CancellationToken.None));

            user.SoftDelete("admin", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        await using var verifyScope = fixture.CreateScope();
        var repository = verifyScope.ServiceProvider.GetRequiredService<IUserRepository>();

        Assert.False(await repository.IsUsernameTakenAsync(username, null, CancellationToken.None));
        Assert.False(await repository.IsEmailTakenAsync($"{username}@example.com", null, CancellationToken.None));
    }

    [Fact]
    public async Task A_user_does_not_collide_with_their_own_current_values()
    {
        var username = TestData.Username("selfcheck");

        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = TestData.NewUser(username);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Without the exclusion, an edit that leaves the email unchanged would report a conflict with itself.
        Assert.False(await users.IsEmailTakenAsync($"{username}@example.com", user.Id, CancellationToken.None));
        Assert.True(await users.IsEmailTakenAsync($"{username}@example.com", Guid.CreateVersion7(), CancellationToken.None));
    }
}
