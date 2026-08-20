using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Infrastructure.Persistence;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Persistence;

/// <summary>
/// Proves ADR-0013. Two administrators editing one user is a normal weekday; without a concurrency token the
/// second save silently destroys the first and the audit trail presents both as deliberate, sequential edits.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ConcurrencyTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task The_second_of_two_concurrent_updates_is_rejected()
    {
        var username = TestData.Username("concurrent");
        Guid userId;

        await using (var seedScope = fixture.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = TestData.NewUser(username);
            context.Users.Add(user);
            await context.SaveChangesAsync();
            userId = user.Id;
        }

        // Two independent scopes, so two change trackers - exactly what two simultaneous requests would have.
        await using var firstScope = fixture.CreateScope();
        await using var secondScope = fixture.CreateScope();

        var firstContext = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var firstCopy = await firstContext.Users.SingleAsync(user => user.Id == userId);
        var secondCopy = await secondContext.Users.SingleAsync(user => user.Id == userId);

        firstCopy.UpdateProfile("First", "Writer", $"first-{username}@example.com");
        await firstContext.SaveChangesAsync();

        secondCopy.UpdateProfile("Second", "Writer", $"second-{username}@example.com");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());

        // The first writer's values survive: the conflict is reported, not resolved by overwriting.
        await using var verifyScope = fixture.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await verifyContext.Users.SingleAsync(user => user.Id == userId);

        Assert.Equal("First", persisted.FirstName);
    }

    [Fact]
    public async Task The_row_version_changes_on_every_successful_update()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(TestData.Username("rowversion"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var initial = user.RowVersion;
        Assert.NotNull(initial);
        Assert.NotEmpty(initial);

        user.UpdateProfile("Changed", "Name", user.Email);
        await context.SaveChangesAsync();

        // A token that never moves is a token that never protects anything.
        Assert.NotEqual(initial, user.RowVersion);
    }

    [Fact]
    public async Task A_stale_token_replayed_from_an_earlier_read_is_rejected()
    {
        var username = TestData.Username("staletoken");
        Guid userId;
        byte[] staleToken;

        await using (var scope = fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = TestData.NewUser(username);
            context.Users.Add(user);
            await context.SaveChangesAsync();

            userId = user.Id;
            staleToken = user.RowVersion!;

            // Someone else updates the row, so the captured token no longer matches.
            user.UpdateProfile("Someone", "Else", user.Email);
            await context.SaveChangesAsync();
        }

        await using var replayScope = fixture.CreateScope();
        var replayContext = replayScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var loaded = await replayContext.Users.SingleAsync(user => user.Id == userId);
        loaded.UpdateProfile("Replayed", "Update", loaded.Email);

        // This is what an API request carrying an out-of-date rowVersion from a form does: the client's token
        // is applied to the entry, and the UPDATE then matches zero rows.
        replayContext.Entry(loaded).Property(nameof(loaded.RowVersion)).OriginalValue = staleToken;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => replayContext.SaveChangesAsync());
    }
}
