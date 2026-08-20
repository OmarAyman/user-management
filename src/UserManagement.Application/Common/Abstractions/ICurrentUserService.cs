namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// The authenticated caller, as read from the validated token. Implemented in the API layer because it reads
/// <c>HttpContext</c>; declared here so use cases consume identity as plain values and never touch ASP.NET
/// Core types.
/// </summary>
/// <remarks>
/// This is the only source of caller identity. Nothing in the application trusts an identifier that arrived in
/// a request body or route - which is what makes IDOR structurally impossible on the self-service routes.
/// </remarks>
public interface ICurrentUserService
{
    /// <summary>The <c>sub</c> claim, or null when the request is anonymous.</summary>
    Guid? UserId { get; }

    string? Username { get; }

    /// <summary>The single <c>role</c> claim value.</summary>
    string? Role { get; }

    bool IsAuthenticated { get; }
}
