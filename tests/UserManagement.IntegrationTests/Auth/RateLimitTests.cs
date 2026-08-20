using System.Net;
using System.Net.Http.Json;
using UserManagement.Domain.Constants;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Auth;

/// <summary>
/// Proves the auth rate limiter. Runs on its own host with a deliberately tiny permit limit: sharing the
/// default host would either need a hundred requests or make every other auth test flaky.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RateLimitTests(ApiFixture fixture)
{
    [Fact]
    public async Task Repeated_sign_in_attempts_from_one_client_are_throttled()
    {
        await using var api = fixture.CreateApi(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["RateLimiting:AuthPermitLimit"] = "3",
            ["RateLimiting:AuthWindowSeconds"] = "60",

            // Seeding is already done by the shared host on this same database.
            ["Seed:Enabled"] = "false",
            ["Database:MigrateOnStartup"] = "false",
        });

        using var client = api.CreateCookieClient();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                new Uri("/api/auth/login", UriKind.Relative),
                new { username = $"nobody-{Guid.NewGuid():N}", password = "WrongPassword@1" });

            statuses.Add(response.StatusCode);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Rejections use the same ProblemDetails shape as every other error, so a client needs no
                // second parser for this case.
                Assert.Equal(ErrorCodes.RateLimited, await response.ReadErrorCodeAsync());
                Assert.NotNull(response.Headers.RetryAfter);
            }
        }

        // Lockout protects one account from many guesses; this protects many accounts from one source. The
        // attempts here all use different usernames, which lockout cannot see at all.
        Assert.Equal(3, statuses.Count(status => status == HttpStatusCode.Unauthorized));
        Assert.Equal(2, statuses.Count(status => status == HttpStatusCode.TooManyRequests));
    }
}
