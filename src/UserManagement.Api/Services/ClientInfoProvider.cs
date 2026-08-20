using UserManagement.Api.Middleware;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Constants;

namespace UserManagement.Api.Services;

/// <summary>
/// Supplies the caller's IP address and correlation id to the audit trail.
/// </summary>
/// <remarks>
/// The address comes from the connection, not from a header. <c>X-Forwarded-For</c> is honoured only when the
/// host is configured with known proxies - forwarded headers are client-controlled text, and treating them as
/// truth by default would let anyone write whatever they liked into the audit trail's IP column.
/// </remarks>
public sealed class ClientInfoProvider(IHttpContextAccessor httpContextAccessor) : IClientInfoProvider
{
    public string IpAddress =>
        httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        ?? SystemActors.UnknownIpAddress;

    public string? CorrelationId =>
        httpContextAccessor.HttpContext?.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var value) == true
            ? value as string
            : null;
}
