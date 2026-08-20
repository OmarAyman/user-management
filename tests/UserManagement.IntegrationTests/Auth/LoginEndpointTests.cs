using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Domain.Constants;
using UserManagement.Infrastructure.Persistence;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class LoginEndpointTests(ApiFixture fixture)
{
    [Theory]
    [InlineData(DemoCredentials.AdminUsername, DemoCredentials.AdminPassword, RoleNames.Admin)]
    [InlineData(DemoCredentials.UserUsername, DemoCredentials.UserPassword, RoleNames.User)]
    [InlineData(DemoCredentials.ReadOnlyUsername, DemoCredentials.ReadOnlyPassword, RoleNames.ReadOnlyUser)]
    public async Task All_three_demo_accounts_can_sign_in(string username, string password, string expectedRole)
    {
        using var client = fixture.Api.CreateCookieClient();

        var payload = await client.LoginAsync(username, password);

        Assert.False(string.IsNullOrWhiteSpace(payload.AccessToken));
        Assert.Equal(username, payload.User.Username);
        Assert.Equal(expectedRole, payload.User.Role);
        Assert.True(payload.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task The_response_body_never_carries_the_refresh_token_or_a_password_hash()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = DemoCredentials.AdminUsername, password = DemoCredentials.AdminPassword });

        var body = await response.Content.ReadAsStringAsync();

        // The refresh token exists only in the cookie. A copy in the body would be readable by any script on
        // the page, which is the exact risk the httpOnly cookie exists to remove.
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_in_sets_an_httponly_samesite_strict_cookie_scoped_to_the_auth_path()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = DemoCredentials.AdminUsername, password = DemoCredentials.AdminPassword });

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("refreshToken=", StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_wrong_password_is_rejected_with_the_generic_code()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = DemoCredentials.UserUsername, password = "WrongPassword@1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidCredentials, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task An_unknown_username_is_indistinguishable_from_a_wrong_password()
    {
        using var client = fixture.Api.CreateCookieClient();

        var unknown = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = $"nobody-{Guid.NewGuid():N}", password = "WrongPassword@1" });

        var wrongPassword = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = DemoCredentials.UserUsername, password = "WrongPassword@1" });

        // Same status, same code, same title: the endpoint is not a user-enumeration oracle.
        Assert.Equal(wrongPassword.StatusCode, unknown.StatusCode);
        Assert.Equal(await wrongPassword.ReadErrorCodeAsync(), await unknown.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Lockout_is_disclosed_only_after_the_correct_password_is_supplied()
    {
        var username = await SeedUserAsync("lockout", DemoCredentials.UserPassword);

        using var client = fixture.Api.CreateCookieClient();

        // Five wrong attempts. Each one is INVALID_CREDENTIALS: an attacker learns nothing about the lockout.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failure = await client.PostAsJsonAsync(
                new Uri("/api/auth/login", UriKind.Relative),
                new { username, password = "WrongPassword@1" });

            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
            Assert.Equal(ErrorCodes.InvalidCredentials, await failure.ReadErrorCodeAsync());
        }

        // Correct password now: the caller has proved they know it, so telling them the account is locked
        // reveals nothing they did not already know - and is the only honest answer (ADR-0006).
        var locked = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username, password = DemoCredentials.UserPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(ErrorCodes.AccountLocked, await locked.ReadErrorCodeAsync());

        var problem = await locked.ReadProblemAsync();
        Assert.True(problem.TryGetProperty("retryAfterSeconds", out var retryAfter));
        Assert.True(retryAfter.GetInt32() > 0);
        Assert.NotNull(locked.Headers.RetryAfter);
    }

    [Fact]
    public async Task A_still_locked_account_reports_invalid_credentials_for_a_wrong_password()
    {
        var username = await SeedUserAsync("lockedwrong", DemoCredentials.UserPassword);

        using var client = fixture.Api.CreateCookieClient();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            await client.PostAsJsonAsync(
                new Uri("/api/auth/login", UriKind.Relative),
                new { username, password = "WrongPassword@1" });
        }

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username, password = "StillWrong@1" });

        Assert.Equal(ErrorCodes.InvalidCredentials, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task A_soft_deleted_user_cannot_sign_in_even_with_the_right_password()
    {
        var username = await SeedUserAsync("deletedlogin", DemoCredentials.UserPassword, softDelete: true);

        using var client = fixture.Api.CreateCookieClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username, password = DemoCredentials.UserPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Generic on purpose: a removed account must not be discoverable (BR-05).
        Assert.Equal(ErrorCodes.InvalidCredentials, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task An_empty_username_is_a_validation_error_with_a_field_level_message()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, await response.ReadErrorCodeAsync());

        var problem = await response.ReadProblemAsync();
        var errors = problem.GetProperty("errors");

        // Field names match the JSON the client sent, so a form can put the message next to the input.
        Assert.True(errors.TryGetProperty("username", out _));
        Assert.True(errors.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task A_payload_with_an_unexpected_field_is_rejected()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = DemoCredentials.AdminUsername, password = DemoCredentials.AdminPassword, role = "Admin" });

        // Unmapped members are refused rather than silently dropped, so a mass-assignment attempt is visible.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers_and_a_correlation_id()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.NotEmpty(response.Headers.GetValues("X-Correlation-Id"));
    }

    /// <summary>Creates a user through the database so a test can lock or delete it without side effects.</summary>
    private async Task<string> SeedUserAsync(string prefix, string password, bool softDelete = false)
    {
        var username = TestData.Username(prefix);

        using var scope = fixture.Api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider
            .GetRequiredService<Application.Common.Abstractions.IPasswordHasher>();

        var user = Domain.Entities.User.Create(
            username,
            $"{username}@example.com",
            "Test",
            "User",
            hasher.Hash(password),
            RoleIds.User);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        if (softDelete)
        {
            user.SoftDelete("admin", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        return username;
    }
}
