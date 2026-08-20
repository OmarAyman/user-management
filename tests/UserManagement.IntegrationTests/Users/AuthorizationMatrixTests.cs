using System.Net;
using System.Net.Http.Json;
using UserManagement.Domain.Constants;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Users;

/// <summary>
/// The authorization matrix, executable.
/// </summary>
/// <remarks>
/// The document in docs/07-authorization-matrix.md is prose; this is the same table as a test, so a capability
/// cannot quietly widen. Every mutating endpoint is called by all three roles, and the UI's opinion about what
/// to show is irrelevant here - these calls bypass the SPA entirely, which is the point.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AuthorizationMatrixTests(ApiFixture fixture)
{
    public enum Role
    {
        Admin,
        User,
        ReadOnly,
    }

    [Theory]
    // Reading the user list is open to every authenticated role.
    [InlineData(Role.Admin, "GET", "/api/users", HttpStatusCode.OK)]
    [InlineData(Role.User, "GET", "/api/users", HttpStatusCode.OK)]
    [InlineData(Role.ReadOnly, "GET", "/api/users", HttpStatusCode.OK)]

    // So is the role list, because it feeds the filter.
    [InlineData(Role.Admin, "GET", "/api/roles", HttpStatusCode.OK)]
    [InlineData(Role.User, "GET", "/api/roles", HttpStatusCode.OK)]
    [InlineData(Role.ReadOnly, "GET", "/api/roles", HttpStatusCode.OK)]

    // Everyone may read their own profile, including the read-only role: "read-only" refers to other people's
    // data, and the assignment's matrix grants self-profile editing to all three.
    [InlineData(Role.Admin, "GET", "/api/users/me", HttpStatusCode.OK)]
    [InlineData(Role.User, "GET", "/api/users/me", HttpStatusCode.OK)]
    [InlineData(Role.ReadOnly, "GET", "/api/users/me", HttpStatusCode.OK)]

    // Deleted users are personal data of removed people: Admin only.
    [InlineData(Role.Admin, "GET", "/api/users/deleted", HttpStatusCode.OK)]
    [InlineData(Role.User, "GET", "/api/users/deleted", HttpStatusCode.Forbidden)]
    [InlineData(Role.ReadOnly, "GET", "/api/users/deleted", HttpStatusCode.Forbidden)]
    public async Task Read_endpoints_follow_the_matrix(
        Role role,
        string method,
        string path,
        HttpStatusCode expected)
    {
        using var client = await ClientForAsync(role);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.User)]
    [InlineData(Role.ReadOnly)]
    public async Task Creating_a_user_is_refused_for_every_non_admin_role(Role role)
    {
        using var client = await ClientForAsync(role);

        var response = await client.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            new
            {
                username = TestData.Username("forbidden"),
                email = $"{TestData.Username("forbidden")}@example.com",
                firstName = "Should",
                lastName = "Fail",
                password = "Whatever@123456",
                roleId = RoleIds.User,
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.User)]
    [InlineData(Role.ReadOnly)]
    public async Task Editing_another_user_is_refused_for_every_non_admin_role(Role role)
    {
        // The IDOR attempt the assignment names explicitly: PUT /api/users/{someoneElse}.
        var target = await CreateUserAsAdminAsync("idortarget");

        using var client = await ClientForAsync(role);

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/users/{target.Id}", UriKind.Relative),
            new
            {
                email = "hijacked@example.com",
                firstName = "Hi",
                lastName = "Jacked",
                roleId = RoleIds.Admin,
                rowVersion = target.RowVersion,
            });

        // Refused by the policy before the action body runs, so the id is never even parsed.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.User)]
    [InlineData(Role.ReadOnly)]
    public async Task Deleting_a_user_is_refused_for_every_non_admin_role(Role role)
    {
        var target = await CreateUserAsAdminAsync("deletetarget");

        using var client = await ClientForAsync(role);

        var response = await client.DeleteAsync(new Uri($"/api/users/{target.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.User)]
    [InlineData(Role.ReadOnly)]
    public async Task Restoring_a_user_is_refused_for_every_non_admin_role(Role role)
    {
        var target = await CreateUserAsAdminAsync("restoretarget");

        using var client = await ClientForAsync(role);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/users/{target.Id}/restore", UriKind.Relative),
            new { rowVersion = target.RowVersion });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_can_perform_every_mutation()
    {
        using var admin = fixture.Api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();

        var created = await CreateUserAsAdminAsync("adminmutations");

        var updated = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{created.Id}", UriKind.Relative),
            new
            {
                email = $"{TestData.Username("updated")}@example.com",
                firstName = "Updated",
                lastName = "Name",
                roleId = RoleIds.ReadOnlyUser,
                rowVersion = created.RowVersion,
            });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var afterUpdate = await updated.Content.ReadFromJsonAsync<UserDetailsPayload>();
        Assert.NotNull(afterUpdate);
        Assert.Equal(RoleNames.ReadOnlyUser, afterUpdate.Role);

        var deleted = await admin.DeleteAsync(new Uri($"/api/users/{created.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Re-read to get the version the delete produced: the delete changed the row, so the token the client
        // held is now stale by design.
        var reloaded = await admin.GetFromJsonAsync<UserDetailsPayload>(
            new Uri($"/api/users/{created.Id}", UriKind.Relative));
        Assert.NotNull(reloaded);

        var restored = await admin.PostAsJsonAsync(
            new Uri($"/api/users/{created.Id}/restore", UriKind.Relative),
            new { rowVersion = reloaded.RowVersion });

        Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_rejected_with_401_not_403()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.GetAsync(new Uri("/api/users", UriKind.Relative));

        // 401 says "authenticate"; 403 would say "you are known and not allowed". Getting this wrong sends a
        // client into a redirect loop or a dead end.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_or_expired_token_is_rejected()
    {
        using var client = fixture.Api.CreateCookieClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not.a.real.token");

        var response = await client.GetAsync(new Uri("/api/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> ClientForAsync(Role role)
    {
        var client = fixture.Api.CreateCookieClient();

        _ = role switch
        {
            Role.Admin => await client.AuthenticateAsAdminAsync(),
            Role.User => await client.AuthenticateAsUserAsync(),
            Role.ReadOnly => await client.AuthenticateAsReadOnlyAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

        return client;
    }

    private async Task<UserDetailsPayload> CreateUserAsAdminAsync(string prefix)
    {
        using var admin = fixture.Api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();

        var username = TestData.Username(prefix);

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            new
            {
                username,
                email = $"{username}@example.com",
                firstName = "Target",
                lastName = "User",
                password = "Target@123456",
                roleId = RoleIds.User,
            });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserDetailsPayload>()
               ?? throw new InvalidOperationException("Create returned no payload.");
    }
}

/// <summary>The detail response, as a client sees it.</summary>
public sealed record UserDetailsPayload(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    int RoleId,
    string Role,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? LastModifiedAt,
    string? LastModifiedBy,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DeletedAt,
    string? DeletedBy,
    string RowVersion);
