using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Enums;
using UserManagement.Infrastructure.Persistence;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Auth;

/// <summary>
/// The refresh-token lifecycle over HTTP: rotation, reuse detection, family revocation and sign-out.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RefreshSessionTests(ApiFixture fixture)
{
    private static readonly Uri RefreshUri = new("/api/auth/refresh", UriKind.Relative);
    private static readonly Uri LogoutUri = new("/api/auth/logout", UriKind.Relative);

    [Fact]
    public async Task Refresh_issues_a_new_access_token_and_rotates_the_cookie()
    {
        using var client = fixture.Api.CreateCookieClient();
        var signIn = await client.LoginAsync(DemoCredentials.AdminUsername, DemoCredentials.AdminPassword);

        var response = await client.PostAsync(RefreshUri, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var refreshed = await response.Content.ReadFromJsonAsync<LoginPayload>();
        Assert.NotNull(refreshed);
        Assert.Equal(signIn.User.Id, refreshed.User.Id);

        // A rotation must replace the cookie, or the client would keep presenting a token that is now revoked.
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("refreshToken=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refresh_without_a_cookie_is_rejected()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.PostAsync(RefreshUri, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidCredentials, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Presenting_a_rotated_token_twice_revokes_the_whole_family()
    {
        // Two clients so the stolen cookie can be replayed after the legitimate client has rotated it.
        using var victim = fixture.Api.CreateCookieClient();
        using var thief = fixture.Api.CreateCookieClient();

        var signIn = await victim.LoginAsync(DemoCredentials.UserUsername, DemoCredentials.UserPassword);
        var stolenCookie = ExtractRefreshCookie(await victim.PostAsync(RefreshUri, null));

        // The victim rotates again, so the stolen token now has a successor.
        var legitimate = await victim.PostAsync(RefreshUri, null);
        Assert.Equal(HttpStatusCode.OK, legitimate.StatusCode);

        thief.DefaultRequestHeaders.Add("Cookie", $"refreshToken={stolenCookie}");
        var replay = await thief.PostAsync(RefreshUri, null);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // Reuse is treated as theft: the entire lineage dies, so the legitimate client is signed out too.
        var afterDetection = await victim.PostAsync(RefreshUri, null);
        Assert.Equal(HttpStatusCode.Unauthorized, afterDetection.StatusCode);

        using var scope = fixture.Api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var family = await context.RefreshTokens
            .AsNoTracking()
            .Where(token => token.UserId == signIn.User.Id)
            .ToListAsync();

        Assert.Contains(family, token => token.RevocationReason == RevocationReason.ReuseDetected);
    }

    [Fact]
    public async Task A_reuse_in_one_family_leaves_another_session_alone()
    {
        // Separate sign-ins are separate families, which is the point of grouping tokens at all: a compromise
        // on one device must not sign the user out everywhere.
        using var firstDevice = fixture.Api.CreateCookieClient();
        using var secondDevice = fixture.Api.CreateCookieClient();
        using var thief = fixture.Api.CreateCookieClient();

        await firstDevice.LoginAsync(DemoCredentials.ReadOnlyUsername, DemoCredentials.ReadOnlyPassword);
        await secondDevice.LoginAsync(DemoCredentials.ReadOnlyUsername, DemoCredentials.ReadOnlyPassword);

        var stolen = ExtractRefreshCookie(await firstDevice.PostAsync(RefreshUri, null));
        await firstDevice.PostAsync(RefreshUri, null);

        thief.DefaultRequestHeaders.Add("Cookie", $"refreshToken={stolen}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.PostAsync(RefreshUri, null)).StatusCode);

        var secondDeviceStillWorks = await secondDevice.PostAsync(RefreshUri, null);
        Assert.Equal(HttpStatusCode.OK, secondDeviceStillWorks.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_is_idempotent()
    {
        using var client = fixture.Api.CreateCookieClient();
        await client.LoginAsync(DemoCredentials.AdminUsername, DemoCredentials.AdminPassword);

        var logout = await client.PostAsync(LogoutUri, null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.PostAsync(RefreshUri, null);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);

        // Signing out twice is not an error: a client that cannot clear a session it no longer has would be
        // stuck reporting a failure for something already true.
        var again = await client.PostAsync(LogoutUri, null);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task A_deleted_user_cannot_refresh_an_existing_session()
    {
        using var client = fixture.Api.CreateCookieClient();
        var signIn = await client.LoginAsync(DemoCredentials.UserUsername, DemoCredentials.UserPassword);

        Guid userId = signIn.User.Id;

        using (var scope = fixture.Api.CreateScope())
        {
            var users = scope.ServiceProvider
                .GetRequiredService<Application.Common.Abstractions.IUserRepository>();
            var unitOfWork = scope.ServiceProvider
                .GetRequiredService<Application.Common.Abstractions.IUnitOfWork>();

            var user = await users.GetByIdAsync(userId, CancellationToken.None);
            user!.SoftDelete("admin", DateTimeOffset.UtcNow);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        try
        {
            var response = await client.PostAsync(RefreshUri, null);

            // The access token stays valid until it expires - that residual window is documented as T-04 -
            // but the session cannot be extended past it.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            using var scope = fixture.Api.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.Contains(
                await context.RefreshTokens.AsNoTracking().Where(token => token.UserId == userId).ToListAsync(),
                token => token.RevocationReason == RevocationReason.UserDeleted);
        }
        finally
        {
            // Restored so the shared demo account stays usable for the rest of the suite.
            using var scope = fixture.Api.CreateScope();
            var users = scope.ServiceProvider
                .GetRequiredService<Application.Common.Abstractions.IUserRepository>();
            var unitOfWork = scope.ServiceProvider
                .GetRequiredService<Application.Common.Abstractions.IUnitOfWork>();

            var user = await users.GetByIdIncludingDeletedAsync(userId, CancellationToken.None);
            user!.Restore();
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static string ExtractRefreshCookie(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("refreshToken=", StringComparison.Ordinal));

        return header["refreshToken=".Length..].Split(';')[0];
    }
}
