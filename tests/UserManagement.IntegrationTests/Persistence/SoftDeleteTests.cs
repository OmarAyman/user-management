using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Infrastructure.Persistence;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Persistence;

[Collection(SqlServerCollection.Name)]
public sealed class SoftDeleteTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task A_soft_deleted_user_disappears_from_ordinary_queries()
    {
        var username = TestData.Username("softdelete");
        await CreateThenSoftDeleteAsync(username);

        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The global query filter is the guarantee: nobody had to remember a Where clause.
        Assert.False(await context.Users.AnyAsync(user => user.Username == username));
    }

    [Fact]
    public async Task The_repository_opt_out_is_the_only_way_to_see_a_deleted_user()
    {
        var username = TestData.Username("optout");
        await CreateThenSoftDeleteAsync(username);

        await using var scope = fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        Assert.False(await users.QueryActive().AnyAsync(user => user.Username == username));
        Assert.True(await users.QueryIncludingDeleted().AnyAsync(user => user.Username == username));
    }

    [Fact]
    public async Task Sign_in_lookup_finds_a_deleted_user_so_it_can_refuse_them()
    {
        var username = TestData.Username("deletedlogin");
        await CreateThenSoftDeleteAsync(username);

        await using var scope = fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var found = await users.GetForAuthenticationAsync(username, CancellationToken.None);

        // Sign-in must distinguish "removed account" from "no such account" - both give the caller the same
        // response, but only one of them should also revoke outstanding tokens.
        Assert.NotNull(found);
        Assert.True(found.IsDeleted);
    }

    [Fact]
    public async Task A_restored_user_becomes_visible_again()
    {
        var username = TestData.Username("restore");
        await CreateThenSoftDeleteAsync(username);

        await using (var scope = fixture.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var user = await users.QueryIncludingDeleted()
                .Where(candidate => candidate.Username == username)
                .Select(candidate => candidate.Id)
                .SingleAsync();

            var tracked = await users.GetByIdIncludingDeletedAsync(user, CancellationToken.None);
            tracked!.Restore();
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var verifyScope = fixture.CreateScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var restored = await context.Users.SingleAsync(user => user.Username == username);
        Assert.False(restored.IsDeleted);
        Assert.Null(restored.DeletedAt);
        Assert.Null(restored.DeletedBy);
    }

    [Fact]
    public async Task The_database_refuses_a_delete_flag_that_disagrees_with_its_timestamp()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(TestData.Username("checkconstraint"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Deliberately bypasses the domain method: the claim under test is that the database enforces the
        // invariant too, not merely that the C# path happens to keep the two columns in step.
        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE Users SET IsDeleted = 1, DeletedAt = NULL WHERE Id = {0}",
            user.Id));

        Assert.Contains("CK_Users_DeletedConsistency", exception.Message, StringComparison.Ordinal);
    }

    private async Task CreateThenSoftDeleteAsync(string username)
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(username);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.SoftDelete("admin", DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
    }
}
