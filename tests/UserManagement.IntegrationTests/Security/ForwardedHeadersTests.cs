using System.Net.Http.Json;
using UserManagement.Domain.Constants;
using UserManagement.IntegrationTests.TestSupport;
using UserManagement.IntegrationTests.Users;

namespace UserManagement.IntegrationTests.Security;

/// <summary>
/// Proves that a client cannot choose the IP address the audit trail records.
/// </summary>
/// <remarks>
/// <c>X-Forwarded-For</c> is text a caller writes. If the application honoured it unconditionally, the audit
/// trail's IP column would record whatever an attacker wanted it to say - which is worse than recording
/// nothing, because it looks authoritative. So forwarded headers are processed only for deployments that name
/// the proxies they trust, and these two tests pin both halves of that behaviour.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class ForwardedHeadersTests(ApiFixture fixture)
{
    private const string SpoofedAddress = "203.0.113.99";

    [Fact]
    public async Task A_forwarded_header_is_ignored_when_no_proxy_is_trusted()
    {
        using var admin = fixture.Api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();

        // The default host trusts no proxy, which is the configuration a careless deployment inherits.
        admin.DefaultRequestHeaders.Add("X-Forwarded-For", SpoofedAddress);

        var created = await CreateUserAsync(admin, "fwdignored");
        var recorded = await ReadAuditIpAsync(admin, created.Id);

        Assert.NotEqual(SpoofedAddress, recorded);

        // The connection address is what gets recorded - loopback, since the test host is in-process.
        Assert.False(string.IsNullOrWhiteSpace(recorded));
    }

    [Fact]
    public async Task A_forwarded_header_is_honoured_when_the_proxy_is_trusted()
    {
        await using var api = fixture.CreateApi(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Both loopback forms: the in-process test server connects from one or the other depending on the
            // host's address family.
            ["ForwardedHeaders:KnownProxies:0"] = "127.0.0.1",
            ["ForwardedHeaders:KnownProxies:1"] = "::1",
            ["Seed:Enabled"] = "false",
            ["Database:MigrateOnStartup"] = "false",
        });

        using var admin = api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();
        admin.DefaultRequestHeaders.Add("X-Forwarded-For", SpoofedAddress);

        var created = await CreateUserAsync(admin, "fwdtrusted");
        var recorded = await ReadAuditIpAsync(admin, created.Id);

        // Behind a trusted proxy the header is the only way to know the real client, so now it is used.
        Assert.Equal(SpoofedAddress, recorded);
    }

    private static async Task<UserDetailsPayload> CreateUserAsync(HttpClient admin, string prefix)
    {
        var username = TestData.Username(prefix);

        var response = await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), new
        {
            username,
            email = $"{username}@example.com",
            firstName = "Forwarded",
            lastName = "Header",
            password = "Created@123456",
            roleId = RoleIds.User,
        });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserDetailsPayload>()
               ?? throw new InvalidOperationException("Create returned no payload.");
    }

    private static async Task<string> ReadAuditIpAsync(HttpClient admin, Guid userId)
    {
        var page = await admin.GetFromJsonAsync<Audit.AuditPagePayload>(
            new Uri($"/api/audit-logs?entityId={userId}", UriKind.Relative));

        Assert.NotNull(page);

        return page.Items.Single().IpAddress;
    }
}
