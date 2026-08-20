using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace UserManagement.Api.Configuration;

/// <summary>
/// Trusted reverse proxies, for deployments that sit behind one.
/// </summary>
/// <remarks>
/// Empty by default, and that default is the security decision: <c>X-Forwarded-For</c> is client-controlled
/// text, so honouring it unconditionally would let anyone write whatever they liked into the audit trail's IP
/// column and into every failed-login log line. Forwarded headers are processed only when a deployment names
/// the proxies it trusts.
/// </remarks>
public sealed class ForwardedHeadersConfigurationOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Individual proxy addresses, e.g. <c>10.0.0.7</c>.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Proxy networks in CIDR form, e.g. <c>10.0.0.0/8</c>.</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>How many proxy hops to walk back through. One is right for a single load balancer.</summary>
    public int ForwardLimit { get; set; } = 1;

    public bool IsConfigured => KnownProxies.Length > 0 || KnownNetworks.Length > 0;
}

public static class ForwardedHeadersConfiguration
{
    /// <summary>
    /// Builds the middleware options, or returns null when no proxy is trusted.
    /// </summary>
    /// <remarks>
    /// ASP.NET Core trusts loopback by default. That is cleared first: a default that quietly works in some
    /// environments and not others is worse than one that does nothing until asked.
    /// </remarks>
    public static ForwardedHeadersOptions? TryBuild(IConfiguration configuration, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = configuration
            .GetSection(ForwardedHeadersConfigurationOptions.SectionName)
            .Get<ForwardedHeadersConfigurationOptions>() ?? new ForwardedHeadersConfigurationOptions();

        if (!settings.IsConfigured)
        {
            // Said out loud, because the symptom of the opposite mistake is subtle: behind an unconfigured proxy
            // every audited IP becomes the load balancer's, and an operator needs one line in the log to explain
            // it rather than a reading of this file.
            logger.LogInformation(
                "Forwarded headers are not processed: no trusted proxy is configured under {Section}. "
                + "The client address is taken from the connection",
                ForwardedHeadersConfigurationOptions.SectionName);

            return null;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = settings.ForwardLimit,
        };

        options.KnownProxies.Clear();

        // KnownIPNetworks, not the obsolete KnownNetworks: .NET 10 moved this to System.Net.IPNetwork.
        options.KnownIPNetworks.Clear();

        foreach (var proxy in settings.KnownProxies)
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
            else
            {
                // Refusing to boot would be worse than skipping one entry, but a silently ignored proxy would
                // mean client addresses quietly stop resolving, so it is logged as a warning.
                logger.LogWarning("Ignoring unparseable ForwardedHeaders:KnownProxies entry {Value}", proxy);
            }
        }

        foreach (var network in settings.KnownNetworks)
        {
            var parts = network.Split('/', 2);

            if (parts.Length == 2
                && IPAddress.TryParse(parts[0], out var prefix)
                && int.TryParse(parts[1], out var prefixLength))
            {
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
            }
            else
            {
                logger.LogWarning("Ignoring unparseable ForwardedHeaders:KnownNetworks entry {Value}", network);
            }
        }

        logger.LogInformation(
            "Forwarded headers enabled for {ProxyCount} proxies and {NetworkCount} networks",
            options.KnownProxies.Count,
            options.KnownIPNetworks.Count);

        return options;
    }
}
