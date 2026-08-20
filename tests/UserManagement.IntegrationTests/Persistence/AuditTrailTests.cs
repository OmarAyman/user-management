using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Enums;
using UserManagement.Infrastructure.Persistence;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Persistence;

/// <summary>
/// Proves the audit policy end to end through the interceptor: which actions are recorded, what identity they
/// carry, and - most importantly - what never reaches storage.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuditTrailTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Creating_a_user_writes_an_insert_row_without_anyone_asking()
    {
        var actorId = Guid.CreateVersion7();
        fixture.CurrentUser.SignInAs(actorId, "admin", RoleNames.Admin);

        try
        {
            await using var scope = fixture.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = TestData.NewUser(TestData.Username("auditinsert"));
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var row = await SingleRowAsync(context, user.Id, AuditAction.Insert);

            Assert.Equal("User", row.EntityName);
            Assert.Equal(user.Id.ToString(), row.EntityId);
            Assert.Equal(user.Username, row.EntityDisplayName);
            Assert.Equal(actorId, row.PerformedByUserId);
            Assert.Equal("admin", row.PerformedByUsername);
            Assert.Equal(fixture.ClientInfo.IpAddress, row.IpAddress);
            Assert.Equal(fixture.ClientInfo.CorrelationId, row.CorrelationId);
            Assert.Null(row.OldValues);
            Assert.NotNull(row.NewValues);
        }
        finally
        {
            fixture.CurrentUser.SignOut();
        }
    }

    [Fact]
    public async Task A_soft_delete_is_recorded_as_a_delete_not_an_update()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(TestData.Username("auditdelete"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.SoftDelete("admin", DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        var actions = await ActionsForAsync(context, user.Id);

        // Physically an update; recorded as a deletion, because the trail records intent.
        Assert.Contains(AuditAction.Delete, actions);
        Assert.DoesNotContain(AuditAction.Update, actions);
    }

    [Fact]
    public async Task A_restore_is_recorded_as_a_restore()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(TestData.Username("auditrestore"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.SoftDelete("admin", DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        user.Restore();
        await context.SaveChangesAsync();

        Assert.Contains(AuditAction.Restore, await ActionsForAsync(context, user.Id));
    }

    [Fact]
    public async Task A_role_change_writes_a_dedicated_row_in_addition_to_the_update()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(TestData.Username("auditrole"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.ChangeRole(RoleIds.Admin);
        await context.SaveChangesAsync();

        var actions = await ActionsForAsync(context, user.Id);

        // Duplication is the point: privilege movement must be findable with a single-column filter.
        Assert.Contains(AuditAction.Update, actions);
        Assert.Contains(AuditAction.RoleChange, actions);

        var roleRow = await SingleRowAsync(context, user.Id, AuditAction.RoleChange);
        Assert.Contains("\"roleId\":2", roleRow.OldValues, StringComparison.Ordinal);
        Assert.Contains("\"roleId\":1", roleRow.NewValues, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Password_material_never_reaches_an_audit_row()
    {
        const string newHash = "AQAAAAIAAYagAAAAELeaked-hash-value-that-must-not-be-stored";

        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(TestData.Username("auditpassword"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.SetPasswordHash(newHash);
        await context.SaveChangesAsync();

        var rows = await context.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityId == user.Id.ToString())
            .ToListAsync();

        Assert.NotEmpty(rows);

        foreach (var row in rows)
        {
            var payload = $"{row.OldValues}{row.NewValues}";

            Assert.DoesNotContain(newHash, payload, StringComparison.Ordinal);
            Assert.DoesNotContain("hash-placeholder", payload, StringComparison.Ordinal);
            Assert.DoesNotContain(user.SecurityStamp.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        }

        // The event is still visible - that is why the field is redacted rather than excluded.
        var updateRow = await SingleRowAsync(context, user.Id, AuditAction.Update);
        Assert.Contains($"\"passwordHash\":\"{AuditRedaction.RedactedMarker}\"", updateRow.NewValues, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_change_to_excluded_columns_alone_writes_no_audit_row()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(TestData.Username("auditnoise"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var before = await context.AuditLogs.CountAsync(log => log.EntityId == user.Id.ToString());

        // Exactly what a sign-in does: stamps LastLoginAt and clears the failure counters. High-churn, and not
        // an audit event - it belongs in the security log instead.
        user.RecordSuccessfulLogin(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        var after = await context.AuditLogs.CountAsync(log => log.EntityId == user.Id.ToString());

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Refresh_tokens_are_never_audited()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(TestData.Username("audittoken"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var token = Domain.Entities.RefreshToken.Issue(
            user.Id,
            Guid.CreateVersion7(),
            new string('d', 64),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7),
            "203.0.113.24");

        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();

        // Token lifecycle is mechanical and would flood the trail; those events go to the security log.
        Assert.False(await context.AuditLogs.AnyAsync(log => log.EntityName == "RefreshToken"));
        Assert.False(await context.AuditLogs.AnyAsync(log =>
            log.NewValues != null && log.NewValues.Contains("dddddddd")));
    }

    [Fact]
    public async Task Existing_audit_rows_are_untouched_by_later_changes()
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(TestData.Username("auditimmutable"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var original = await context.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityId == user.Id.ToString())
            .Select(log => new { log.Id, log.NewValues, log.Timestamp, log.Action })
            .ToListAsync();

        user.UpdateProfile("Changed", "Again", user.Email);
        await context.SaveChangesAsync();

        user.SoftDelete("admin", DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        var afterwards = await context.AuditLogs
            .AsNoTracking()
            .Where(log => original.Select(row => row.Id).Contains(log.Id))
            .Select(log => new { log.Id, log.NewValues, log.Timestamp, log.Action })
            .ToListAsync();

        Assert.Equal(original.OrderBy(row => row.Id), afterwards.OrderBy(row => row.Id));
    }

    [Fact]
    public async Task An_unauthenticated_operation_is_attributed_to_the_system()
    {
        fixture.CurrentUser.SignOut();

        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = TestData.NewUser(TestData.Username("auditsystem"));
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var row = await SingleRowAsync(context, user.Id, AuditAction.Insert);

        Assert.Null(row.PerformedByUserId);
        Assert.Equal(SystemActors.System, row.PerformedByUsername);
    }

    private static async Task<Domain.Entities.AuditLog> SingleRowAsync(
        ApplicationDbContext context,
        Guid userId,
        AuditAction action) =>
        await context.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityId == userId.ToString() && log.Action == action)
            .OrderByDescending(log => log.Id)
            .FirstAsync();

    private static async Task<List<AuditAction>> ActionsForAsync(ApplicationDbContext context, Guid userId) =>
        await context.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityId == userId.ToString())
            .Select(log => log.Action)
            .ToListAsync();
}
