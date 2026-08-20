using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Constants;

namespace UserManagement.IntegrationTests.TestSupport;

/// <summary>
/// Stands in for the API layer's <c>HttpContext</c>-backed implementation. Mutable so a test can act as a
/// specific administrator and then assert on what the audit trail recorded.
/// </summary>
public sealed class TestCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }

    public string? Username { get; set; }

    public string? Role { get; set; }

    public bool IsAuthenticated => UserId is not null;

    public void SignInAs(Guid userId, string username, string role)
    {
        UserId = userId;
        Username = username;
        Role = role;
    }

    public void SignOut()
    {
        UserId = null;
        Username = null;
        Role = null;
    }
}

public sealed class TestClientInfoProvider : IClientInfoProvider
{
    public string IpAddress { get; set; } = "203.0.113.24";

    public string? CorrelationId { get; set; } = "test-correlation-id";
}
