using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Entities;
using UserManagement.Infrastructure.Configuration;

namespace UserManagement.Infrastructure.Security;

/// <summary>
/// Issues HS256-signed access tokens carrying the four claims the application actually uses.
/// </summary>
/// <remarks>
/// Nothing else goes in the token. Email, names and permissions would all have to be re-issued when they
/// change, and a token is the one place where stale data is invisible until it causes a wrong decision.
/// </remarks>
public sealed class AccessTokenIssuer(IOptions<JwtOptions> options, IDateTimeProvider clock) : IAccessTokenIssuer
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(User user, string roleName)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        var issuedAt = clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtClaimNames.Subject] = user.Id.ToString(),
                [JwtClaimNames.Username] = user.Username,
                [JwtClaimNames.Role] = roleName,
                [JwtClaimNames.TokenId] = Guid.CreateVersion7().ToString(),
            },
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
