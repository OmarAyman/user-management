using System.Net;
using System.Net.Http.Json;
using UserManagement.Domain.Constants;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Users;

/// <summary>CRUD over HTTP, including every business rule a client can trip.</summary>
[Collection(ApiCollection.Name)]
public sealed class UserCrudTests(ApiFixture fixture)
{
    [Fact]
    public async Task Creating_a_user_returns_201_with_a_location_header_and_no_password_material()
    {
        using var admin = await AdminAsync();
        var username = TestData.Username("created");

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            NewUser(username));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", body, StringComparison.Ordinal);

        var created = await response.Content.ReadFromJsonAsync<UserDetailsPayload>();
        Assert.NotNull(created);
        Assert.Equal(username, created.Username);

        // The concurrency token is returned so the client can send it back on update. Without it the client
        // could not prove which version it edited.
        Assert.False(string.IsNullOrWhiteSpace(created.RowVersion));

        // Stamped by the interceptor, not by the handler.
        Assert.Equal(DemoCredentials.AdminUsername, created.CreatedBy);
    }

    [Fact]
    public async Task The_created_user_can_sign_in_with_the_assigned_password()
    {
        using var admin = await AdminAsync();
        var username = TestData.Username("signin");

        (await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), NewUser(username)))
            .EnsureSuccessStatusCode();

        using var client = fixture.Api.CreateCookieClient();
        var payload = await client.LoginAsync(username, "Created@123456");

        // Proves the create path hashed the password with the same scheme sign-in verifies against - the kind
        // of mismatch a unit test on either side alone would miss.
        Assert.Equal(username, payload.User.Username);
    }

    [Theory]
    [InlineData("username")]
    [InlineData("email")]
    public async Task A_duplicate_identifier_is_a_conflict_naming_the_field(string duplicated)
    {
        using var admin = await AdminAsync();
        var first = TestData.Username("dup");

        (await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), NewUser(first)))
            .EnsureSuccessStatusCode();

        var second = TestData.Username("dup2");

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            duplicated == "username"
                ? NewUser(first, email: $"{second}@example.com")
                : NewUser(second, email: $"{first}@example.com"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            duplicated == "username" ? ErrorCodes.UsernameAlreadyExists : ErrorCodes.EmailAlreadyExists,
            await response.ReadErrorCodeAsync());
    }

    [Theory]
    [InlineData("short", "Ab1", "password")]
    [InlineData("nodigit", "NoDigitsHereAtAll", "password")]
    [InlineData("nouppercase", "alllowercase123", "password")]
    [InlineData("bademail", "Created@123456", "email")]
    public async Task Invalid_input_is_rejected_with_a_field_level_message(
        string prefix,
        string password,
        string expectedField)
    {
        using var admin = await AdminAsync();
        var username = TestData.Username(prefix);

        var payload = expectedField == "email"
            ? NewUser(username, email: "not-an-email", password: password)
            : NewUser(username, password: password);

        var response = await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, await response.ReadErrorCodeAsync());

        var problem = await response.ReadProblemAsync();
        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedField, out _));
    }

    [Fact]
    public async Task Creating_a_user_with_an_unknown_role_is_rejected()
    {
        using var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            NewUser(TestData.Username("badrole"), roleId: 99));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_a_user_changes_the_role_and_returns_a_new_concurrency_token()
    {
        using var admin = await AdminAsync();
        var created = await CreateAsync(admin, "roleupdate");

        var response = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{created.Id}", UriKind.Relative),
            new
            {
                email = created.Email,
                firstName = "Promoted",
                lastName = created.LastName,
                roleId = RoleIds.Admin,
                rowVersion = created.RowVersion,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<UserDetailsPayload>();
        Assert.NotNull(updated);
        Assert.Equal(RoleNames.Admin, updated.Role);
        Assert.Equal("Promoted", updated.FirstName);

        // A successful write must produce a new token, or the next update would be accepted with a stale one.
        Assert.NotEqual(created.RowVersion, updated.RowVersion);
        Assert.Equal(DemoCredentials.AdminUsername, updated.LastModifiedBy);
    }

    [Fact]
    public async Task Updating_with_a_stale_concurrency_token_is_refused()
    {
        using var admin = await AdminAsync();
        var created = await CreateAsync(admin, "concurrent");

        var firstWriter = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{created.Id}", UriKind.Relative),
            new
            {
                email = created.Email,
                firstName = "First",
                lastName = created.LastName,
                roleId = created.RoleId,
                rowVersion = created.RowVersion,
            });
        firstWriter.EnsureSuccessStatusCode();

        // The second writer read the row before the first write and is still holding that version.
        var secondWriter = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{created.Id}", UriKind.Relative),
            new
            {
                email = created.Email,
                firstName = "Second",
                lastName = created.LastName,
                roleId = created.RoleId,
                rowVersion = created.RowVersion,
            });

        Assert.Equal(HttpStatusCode.Conflict, secondWriter.StatusCode);
        Assert.Equal(ErrorCodes.ResourceModified, await secondWriter.ReadErrorCodeAsync());

        // The first writer's value survives: the point of the token is that a lost update becomes a visible
        // conflict instead of silent data loss.
        var current = await admin.GetFromJsonAsync<UserDetailsPayload>(
            new Uri($"/api/users/{created.Id}", UriKind.Relative));
        Assert.NotNull(current);
        Assert.Equal("First", current.FirstName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    public async Task Updating_without_a_usable_concurrency_token_is_rejected(string rowVersion)
    {
        using var admin = await AdminAsync();
        var created = await CreateAsync(admin, "notoken");

        var response = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{created.Id}", UriKind.Relative),
            new
            {
                email = created.Email,
                firstName = created.FirstName,
                lastName = created.LastName,
                roleId = created.RoleId,
                rowVersion,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_a_user_that_does_not_exist_is_a_404()
    {
        using var admin = await AdminAsync();

        var response = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{Guid.CreateVersion7()}", UriKind.Relative),
            new
            {
                email = "nobody@example.com",
                firstName = "No",
                lastName = "Body",
                roleId = RoleIds.User,
                rowVersion = Convert.ToBase64String(new byte[8]),
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.ResourceNotFound, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task An_admin_cannot_change_their_own_role()
    {
        using var admin = await AdminAsync();

        var me = await admin.GetFromJsonAsync<UserDetailsPayload>(
            new Uri("/api/users/me", UriKind.Relative));
        Assert.NotNull(me);

        var response = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{me.Id}", UriKind.Relative),
            new
            {
                email = me.Email,
                firstName = me.FirstName,
                lastName = me.LastName,
                roleId = RoleIds.User,
                rowVersion = me.RowVersion,
            });

        // 422, not 403: the payload is well formed and the caller is permitted to edit users - the request is
        // semantically disallowed (BR-04).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.CannotChangeOwnRole, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task An_admin_can_edit_their_own_profile_fields_through_the_admin_route()
    {
        using var admin = await AdminAsync();

        var me = await admin.GetFromJsonAsync<UserDetailsPayload>(
            new Uri("/api/users/me", UriKind.Relative));
        Assert.NotNull(me);

        // Same role, different name: the own-role guard must not turn into "administrators cannot edit
        // themselves at all".
        var response = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{me.Id}", UriKind.Relative),
            new
            {
                email = me.Email,
                firstName = "System",
                lastName = "Administrator",
                roleId = me.RoleId,
                rowVersion = me.RowVersion,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_delete_their_own_account()
    {
        using var admin = await AdminAsync();

        var me = await admin.GetFromJsonAsync<UserDetailsPayload>(
            new Uri("/api/users/me", UriKind.Relative));
        Assert.NotNull(me);

        var response = await admin.DeleteAsync(new Uri($"/api/users/{me.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.CannotDeleteSelf, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Deleting_an_already_deleted_user_is_a_conflict_not_a_404()
    {
        using var admin = await AdminAsync();
        var created = await CreateAsync(admin, "twicedeleted");

        (await admin.DeleteAsync(new Uri($"/api/users/{created.Id}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        var second = await admin.DeleteAsync(new Uri($"/api/users/{created.Id}", UriKind.Relative));

        // An Admin needs to tell "already gone" from "never existed"; 404 would conflate them.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.UserAlreadyDeleted, await second.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Restoring_a_user_that_is_not_deleted_is_a_conflict()
    {
        using var admin = await AdminAsync();
        var created = await CreateAsync(admin, "notdeleted");

        var response = await admin.PostAsJsonAsync(
            new Uri($"/api/users/{created.Id}/restore", UriKind.Relative),
            new { rowVersion = created.RowVersion });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.UserNotDeleted, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task A_deleted_username_can_be_reused_and_the_restore_then_conflicts()
    {
        using var admin = await AdminAsync();

        var username = TestData.Username("reused");
        var original = await CreateAsync(admin, "reused", username);

        (await admin.DeleteAsync(new Uri($"/api/users/{original.Id}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        // Uniqueness is scoped to active users, so deletion releases the identifiers (ADR-0009).
        var replacement = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            NewUser(username, email: $"{username}@example.com"));

        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);

        var reloaded = await admin.GetFromJsonAsync<UserDetailsPayload>(
            new Uri($"/api/users/{original.Id}", UriKind.Relative));
        Assert.NotNull(reloaded);

        var restore = await admin.PostAsJsonAsync(
            new Uri($"/api/users/{original.Id}/restore", UriKind.Relative),
            new { rowVersion = reloaded.RowVersion });

        // The failure mode the reversal introduces, handled deliberately rather than surfacing as a database
        // error (BR-17).
        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
        Assert.Equal(ErrorCodes.UsernameAlreadyExists, await restore.ReadErrorCodeAsync());
    }

    // BR-02 and BR-03 - the last-administrator guard - are covered by unit tests instead of here. Proving them
    // over HTTP requires reducing the whole system to one administrator, and these tests share one database, so
    // the setup would break every other test in the suite. The handler test controls the admin count directly.
    private static object NewUser(
        string username,
        string? email = null,
        string password = "Created@123456",
        int roleId = RoleIds.User) => new
        {
            username,
            email = email ?? $"{username}@example.com",
            firstName = "Test",
            lastName = "User",
            password,
            roleId,
        };

    private async Task<HttpClient> AdminAsync()
    {
        var client = fixture.Api.CreateCookieClient();
        await client.AuthenticateAsAdminAsync();

        return client;
    }

    private static async Task<UserDetailsPayload> CreateAsync(
        HttpClient admin,
        string prefix,
        string? username = null,
        int roleId = RoleIds.User)
    {
        var name = username ?? TestData.Username(prefix);

        var response = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            NewUser(name, roleId: roleId));

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserDetailsPayload>()
               ?? throw new InvalidOperationException("Create returned no payload.");
    }
}

