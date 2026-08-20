using System.Net;
using System.Net.Http.Json;
using UserManagement.Domain.Constants;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Errors;

/// <summary>
/// Proves that an error response never hands anything back that it was given in confidence.
/// </summary>
/// <remarks>
/// The audit policy names three places a credential must never reach: audit records, structured logs, and
/// exception responses. The first two have their own tests; this covers the third. An error path is the easiest
/// place for a secret to escape, because the natural way to write a helpful message is to echo the input.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class ErrorResponseDisclosureTests(ApiFixture fixture)
{
    private const string SubmittedPassword = "Submitted!Secret1234";

    [Fact]
    public async Task A_failed_sign_in_does_not_echo_the_password()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = TestData.Username("nobody"), password = SubmittedPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoCredentialMaterialAsync(response);
    }

    [Fact]
    public async Task A_validation_failure_names_the_field_without_quoting_the_value()
    {
        using var admin = fixture.Api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();

        var response = await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), new
        {
            username = TestData.Username("disclose"),
            email = "not-an-email",
            firstName = "No",
            lastName = "Echo",

            // Fails the policy, so the response will describe what is wrong with it.
            password = "weak",
            roleId = RoleIds.User,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.ReadProblemAsync();

        // The field is named and the rule is explained...
        Assert.True(problem.GetProperty("errors").TryGetProperty("password", out _));

        // ...without repeating what was submitted. A validation message that quotes the rejected password puts
        // it into every log, proxy and browser history the response passes through.
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("weak", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_password_change_does_not_echo_either_password()
    {
        using var admin = fixture.Api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();

        var username = TestData.Username("pwdisclose");

        (await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), new
        {
            username,
            email = $"{username}@example.com",
            firstName = "Password",
            lastName = "Echo",
            password = "Created@123456",
            roleId = RoleIds.User,
        })).EnsureSuccessStatusCode();

        using var client = fixture.Api.CreateCookieClient();
        await client.AuthenticateAsync(username, "Created@123456");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/users/me/change-password", UriKind.Relative),
            new { currentPassword = SubmittedPassword, newPassword = "Replacement!Secret1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(SubmittedPassword, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Replacement!Secret1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conflict_response_carries_no_stored_hash()
    {
        using var admin = fixture.Api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();

        var username = TestData.Username("conflictdisclose");

        var payload = new
        {
            username,
            email = $"{username}@example.com",
            firstName = "Conflict",
            lastName = "Echo",
            password = "Created@123456",
            roleId = RoleIds.User,
        };

        (await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), payload)).EnsureSuccessStatusCode();

        var duplicate = await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), payload);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await AssertNoCredentialMaterialAsync(duplicate);
    }

    [Fact]
    public async Task An_unauthenticated_response_reveals_nothing_about_the_server()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.GetAsync(new Uri("/api/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        // No stack frames, no exception type names, no SQL, no connection details.
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_error_response_leaks_the_signing_key_or_a_token()
    {
        using var client = fixture.Api.CreateCookieClient();
        var session = await client.LoginAsync(DemoCredentials.AdminUsername, DemoCredentials.AdminPassword);

        // A malformed token is the request most likely to provoke a helpful-but-leaky message.
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $"{session.AccessToken}-tampered");

        var response = await client.GetAsync(new Uri("/api/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("eyJ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Jwt", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IssuerSigningKey", body, StringComparison.Ordinal);
    }

    private static async Task AssertNoCredentialMaterialAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(SubmittedPassword, body, StringComparison.Ordinal);

        // The ASP.NET v3 hash prefix: its presence would mean a stored hash reached a client.
        Assert.DoesNotContain("AQAAAA", body, StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
    }
}
