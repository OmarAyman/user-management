namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Transport-level facts about the caller, needed by the audit trail and the security log. Implemented in the
/// API layer over <c>HttpContext</c>.
/// </summary>
public interface IClientInfoProvider
{
    /// <summary>
    /// The originating IP address, or <c>"unknown"</c> when there is no HTTP context (the seeder, unit tests).
    /// Forwarded headers are honoured only when known proxies are configured, so a caller cannot spoof this by
    /// sending <c>X-Forwarded-For</c>.
    /// </summary>
    string IpAddress { get; }

    /// <summary>Ties an audit row to the request log. Null outside a request.</summary>
    string? CorrelationId { get; }
}
