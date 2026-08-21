using System.Net;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Security;

/// <summary>
/// The response headers, and the one place the policy has to differ.
/// </summary>
/// <remarks>
/// `default-src 'none'` is right for an API: a JSON response has no business loading anything. It is wrong for
/// the single HTML document this API serves, and that mismatch is how the documented `/swagger` link came to
/// return 200 while rendering a blank page - the browser blocked Swagger UI's own stylesheet and scripts. curl
/// reported success throughout, which is why nothing noticed.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class SecurityHeadersTests(ApiFixture fixture)
{
    [Fact]
    public async Task An_api_response_forbids_loading_anything()
    {
        using var client = fixture.Api.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_api_response_carries_the_rest_of_the_headers()
    {
        using var client = fixture.Api.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));

        // A free version-fingerprinting signal, removed.
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task The_swagger_page_is_allowed_to_load_its_own_assets()
    {
        using var client = fixture.Api.CreateClient();

        var response = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        // The assertion that matters: a 200 proves the page was served, and this proves it can render.
        Assert.DoesNotContain("default-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("style-src 'self'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_swagger_page_still_refuses_framing_and_third_party_origins()
    {
        using var client = fixture.Api.CreateClient();

        var response = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));

        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        // Relaxed enough to work, and no further: same-origin only, and it cannot be framed.
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("*", policy, StringComparison.Ordinal);
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
    }

    [Fact]
    public async Task The_relaxation_does_not_leak_onto_paths_that_merely_start_with_the_same_letters()
    {
        using var client = fixture.Api.CreateClient();

        // "/swaggering" is not "/swagger": StartsWithSegments, not StartsWith.
        var response = await client.GetAsync(new Uri("/swaggering", UriKind.Relative));

        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
    }
}
