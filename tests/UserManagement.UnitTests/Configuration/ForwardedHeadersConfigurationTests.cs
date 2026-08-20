using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UserManagement.Api.Configuration;
using UserManagement.UnitTests.TestSupport;

namespace UserManagement.UnitTests.Configuration;

/// <summary>
/// The parsing and defaulting rules for trusted proxies.
/// </summary>
/// <remarks>
/// Two integration tests already prove the end-to-end behaviour - a forwarded header is ignored with no proxy
/// configured and honoured with one. These cover what those cannot reach cheaply: that ASP.NET Core's default
/// loopback trust is cleared rather than inherited, that a malformed entry is skipped loudly instead of
/// silently, and that the do-nothing path announces itself. That last one exists because the failure it
/// prevents is quiet: behind an unconfigured proxy every audited IP becomes the load balancer's, and without a
/// startup line an operator has no thread to pull.
/// </remarks>
public sealed class ForwardedHeadersConfigurationTests
{
    [Fact]
    public void No_configuration_means_no_forwarded_header_processing()
    {
        var logger = new CapturingLogger<ForwardedHeadersConfigurationTests>();

        var options = ForwardedHeadersConfiguration.TryBuild(Configure([]), logger);

        Assert.Null(options);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Contains("ForwardedHeaders", logger.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_proxy_is_trusted_and_the_loopback_default_is_not()
    {
        var logger = new CapturingLogger<ForwardedHeadersConfigurationTests>();

        var options = ForwardedHeadersConfiguration.TryBuild(
            Configure([new("ForwardedHeaders:KnownProxies:0", "10.0.0.4")]),
            logger);

        Assert.NotNull(options);

        // Exactly the proxy that was named. ASP.NET Core trusts loopback out of the box, and inheriting that
        // would mean a development machine behaves differently from a deployed one for no stated reason.
        Assert.Equal(["10.0.0.4"], options.KnownProxies.Select(address => address.ToString()));
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void A_named_network_is_parsed_as_a_prefix()
    {
        var logger = new CapturingLogger<ForwardedHeadersConfigurationTests>();

        var options = ForwardedHeadersConfiguration.TryBuild(
            Configure([new("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/24")]),
            logger);

        Assert.NotNull(options);

        var network = Assert.Single(options.KnownIPNetworks);
        Assert.Equal("10.0.0.0", network.BaseAddress.ToString());
        Assert.Equal(24, network.PrefixLength);
    }

    [Fact]
    public void Only_the_forwarded_for_and_proto_headers_are_honoured()
    {
        var logger = new CapturingLogger<ForwardedHeadersConfigurationTests>();

        var options = ForwardedHeadersConfiguration.TryBuild(
            Configure([new("ForwardedHeaders:KnownProxies:0", "10.0.0.4")]),
            logger);

        Assert.NotNull(options);

        // X-Forwarded-Host is deliberately absent: nothing here builds URLs from the request host, and
        // accepting it would widen the trust for no gain.
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
    }

    [Fact]
    public void A_configured_forward_limit_is_respected()
    {
        var logger = new CapturingLogger<ForwardedHeadersConfigurationTests>();

        var options = ForwardedHeadersConfiguration.TryBuild(
            Configure(
            [
                new("ForwardedHeaders:KnownProxies:0", "10.0.0.4"),
                new("ForwardedHeaders:ForwardLimit", "2"),
            ]),
            logger);

        Assert.NotNull(options);
        Assert.Equal(2, options.ForwardLimit);
    }

    [Fact]
    public void An_unparseable_entry_is_skipped_with_a_warning_rather_than_silently()
    {
        var logger = new CapturingLogger<ForwardedHeadersConfigurationTests>();

        var options = ForwardedHeadersConfiguration.TryBuild(
            Configure(
            [
                new("ForwardedHeaders:KnownProxies:0", "not-an-address"),
                new("ForwardedHeaders:KnownProxies:1", "10.0.0.4"),
                new("ForwardedHeaders:KnownNetworks:0", "10.0.0.0"),
            ]),
            logger);

        Assert.NotNull(options);

        // The good entry survives; the bad ones are announced. Refusing to boot over one typo would be worse,
        // but a silently dropped proxy means client addresses stop resolving with nothing to explain it.
        Assert.Equal(["10.0.0.4"], options.KnownProxies.Select(address => address.ToString()));
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
    }

    private static IConfiguration Configure(KeyValuePair<string, string?>[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
