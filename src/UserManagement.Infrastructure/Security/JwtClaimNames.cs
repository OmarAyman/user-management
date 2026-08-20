namespace UserManagement.Infrastructure.Security;

/// <summary>
/// The claim names this application issues and reads. Deliberately short and unmapped: the API disables
/// inbound claim mapping, so what is written here is exactly what arrives in <c>ClaimsPrincipal</c> - no
/// translation to the long WS-Federation URIs, and no guessing which name a lookup should use.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>Subject: the user id.</summary>
    public const string Subject = "sub";

    public const string Username = "username";

    public const string Role = "role";

    /// <summary>Token identifier, so an individual token can be named in a log without quoting the token.</summary>
    public const string TokenId = "jti";
}
