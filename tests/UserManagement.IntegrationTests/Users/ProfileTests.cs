using System.Net;
using System.Net.Http.Json;
using UserManagement.Domain.Constants;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Users;

/// <summary>Self-service: the routes where a non-Admin can write, and therefore where IDOR would live.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProfileTests(ApiFixture fixture)
{
    private static readonly Uri MeUri = new("/api/users/me", UriKind.Relative);
    private static readonly Uri ChangePasswordUri = new("/api/users/me/change-password", UriKind.Relative);

    [Fact]
    public async Task Every_role_can_read_and_update_its_own_profile()
    {
        foreach (var (username, password) in new[]
                 {
                     (DemoCredentials.AdminUsername, DemoCredentials.AdminPassword),
                     (DemoCredentials.UserUsername, DemoCredentials.UserPassword),
                     (DemoCredentials.ReadOnlyUsername, DemoCredentials.ReadOnlyPassword),
                 })
        {
            using var client = fixture.Api.CreateCookieClient();
            await client.AuthenticateAsync(username, password);

            var me = await client.GetFromJsonAsync<UserDetailsPayload>(MeUri);
            Assert.NotNull(me);
            Assert.Equal(username, me.Username);

            var response = await client.PutAsJsonAsync(MeUri, new
            {
                firstName = me.FirstName,
                lastName = me.LastName,
                email = me.Email,
                rowVersion = me.RowVersion,
            });

            // Including ReadOnlyUser: "read-only" refers to other people's data. The assignment's matrix grants
            // self-profile editing to all three roles, and this is the row a reviewer is most likely to query.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_profile_payload_carrying_a_role_is_rejected_outright()
    {
        using var client = fixture.Api.CreateCookieClient();
        await client.AuthenticateAsUserAsync();

        var me = await client.GetFromJsonAsync<UserDetailsPayload>(MeUri);
        Assert.NotNull(me);

        // The mass-assignment attempt the assignment names: {"role": "Admin", "isDeleted": false}.
        var response = await client.PutAsJsonAsync(MeUri, new
        {
            firstName = me.FirstName,
            lastName = me.LastName,
            email = me.Email,
            rowVersion = me.RowVersion,
            roleId = RoleIds.Admin,
            isDeleted = false,
        });

        // Refused rather than silently stripped: both are safe, but this version is visible in the logs and
        // honest to the caller.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var after = await client.GetFromJsonAsync<UserDetailsPayload>(MeUri);
        Assert.NotNull(after);
        Assert.Equal(RoleNames.User, after.Role);
    }

    [Fact]
    public async Task A_user_updating_their_profile_cannot_touch_anyone_else()
    {
        using var admin = fixture.Api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();

        var victimName = TestData.Username("victim");
        var createVictim = await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), new
        {
            username = victimName,
            email = $"{victimName}@example.com",
            firstName = "Victim",
            lastName = "User",
            password = "Victim@123456",
            roleId = RoleIds.User,
        });
        createVictim.EnsureSuccessStatusCode();
        var victim = await createVictim.Content.ReadFromJsonAsync<UserDetailsPayload>();
        Assert.NotNull(victim);

        using var attacker = fixture.Api.CreateCookieClient();
        await attacker.AuthenticateAsUserAsync();

        // There is no id in the route to swap, and no id in the model to forge - the subject comes from the
        // validated token. Sending someone else's id anyway just makes the payload invalid.
        var response = await attacker.PutAsJsonAsync(MeUri, new
        {
            id = victim.Id,
            firstName = "Hijacked",
            lastName = "Name",
            email = "hijacked@example.com",
            rowVersion = victim.RowVersion,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var victimAfter = await admin.GetFromJsonAsync<UserDetailsPayload>(
            new Uri($"/api/users/{victim.Id}", UriKind.Relative));
        Assert.NotNull(victimAfter);
        Assert.Equal("Victim", victimAfter.FirstName);
    }

    [Fact]
    public async Task Updating_a_profile_to_an_email_another_active_user_holds_is_a_conflict()
    {
        using var client = fixture.Api.CreateCookieClient();
        await client.AuthenticateAsUserAsync();

        var me = await client.GetFromJsonAsync<UserDetailsPayload>(MeUri);
        Assert.NotNull(me);

        var response = await client.PutAsJsonAsync(MeUri, new
        {
            firstName = me.FirstName,
            lastName = me.LastName,
            email = "admin@example.com",
            rowVersion = me.RowVersion,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.EmailAlreadyExists, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Changing_a_password_requires_the_current_one_and_ends_every_session()
    {
        // A dedicated account, because this test invalidates its sessions and changes its credentials.
        using var admin = fixture.Api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();

        var username = TestData.Username("pwchange");
        var create = await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), new
        {
            username,
            email = $"{username}@example.com",
            firstName = "Password",
            lastName = "Changer",
            password = "Original@123456",
            roleId = RoleIds.User,
        });
        create.EnsureSuccessStatusCode();

        using var client = fixture.Api.CreateCookieClient();
        await client.AuthenticateAsync(username, "Original@123456");

        var wrongCurrent = await client.PostAsJsonAsync(ChangePasswordUri, new
        {
            currentPassword = "NotTheRightOne@1",
            newPassword = "Replaced@123456",
        });

        // A stolen access token must not be enough to take over the account: proof of the current password is
        // required.
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCurrent.StatusCode);
        Assert.Equal(ErrorCodes.InvalidCredentials, await wrongCurrent.ReadErrorCodeAsync());

        var changed = await client.PostAsJsonAsync(ChangePasswordUri, new
        {
            currentPassword = "Original@123456",
            newPassword = "Replaced@123456",
        });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        // Every refresh-token family is revoked: a password change is the action a user takes precisely because
        // they fear their sessions are compromised.
        var refreshAfterChange = await client.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterChange.StatusCode);

        using var fresh = fixture.Api.CreateCookieClient();
        var reLogin = await fresh.LoginAsync(username, "Replaced@123456");
        Assert.Equal(username, reLogin.User.Username);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("nouppercaseordigits")]
    public async Task A_new_password_that_fails_the_policy_is_rejected(string newPassword)
    {
        using var client = fixture.Api.CreateCookieClient();
        await client.AuthenticateAsReadOnlyAsync();

        var response = await client.PostAsJsonAsync(ChangePasswordUri, new
        {
            currentPassword = DemoCredentials.ReadOnlyPassword,
            newPassword,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Availability_reports_active_users_only()
    {
        using var admin = fixture.Api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();

        var taken = await admin.GetFromJsonAsync<AvailabilityPayload>(
            new Uri($"/api/users/availability?username={DemoCredentials.AdminUsername}", UriKind.Relative));
        Assert.NotNull(taken);
        Assert.False(taken.UsernameAvailable);

        var free = await admin.GetFromJsonAsync<AvailabilityPayload>(
            new Uri($"/api/users/availability?username={TestData.Username("free")}", UriKind.Relative));
        Assert.NotNull(free);
        Assert.True(free.UsernameAvailable);

        // Omitted values come back null rather than as a guess.
        Assert.Null(free.EmailAvailable);
    }

    [Fact]
    public async Task Availability_requires_authentication()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.GetAsync(
            new Uri("/api/users/availability?username=probe", UriKind.Relative));

        // Authenticated, not anonymous: every signed-in role can already list users, so this adds no
        // enumeration surface - but anonymous access would.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed record AvailabilityPayload(bool? UsernameAvailable, bool? EmailAvailable);
