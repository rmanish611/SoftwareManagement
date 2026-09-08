using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SoftwareManagement.Domain.Common;
using SoftwareManagement.Domain.Identity;

namespace SoftwareManagement.Infrastructure.Security;

/// <summary>
/// Issues access tokens and refresh tokens.
///
/// Refresh tokens are random 256-bit values. Only their SHA-256 hash is stored, so a stolen
/// database backup cannot be replayed as a session, and the lookup is still a single indexed
/// equality match (BR-IAM-03).
/// </summary>
public sealed class TokenFactory(IOptions<JwtOptions> options, IClock clock)
{
    private readonly JwtOptions _options = options.Value;
    private readonly IClock _clock = clock;

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(
        AdminUser user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            new(ClaimTypes.Name, user.FullName),
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        // Permissions travel in the token so an endpoint check is a claim comparison rather than a
        // database round trip on every request. A revoked permission takes effect within the
        // 15-minute access-token lifetime, which is why that lifetime is short.
        claims.AddRange(permissions.Select(permission => new Claim(PermissionClaimType, permission)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>The claim type every permission is carried on.</summary>
    public const string PermissionClaimType = "permission";

    public (string Token, string Hash, DateTime ExpiresAtUtc) CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);

        // Base64url, not plain base64: the token travels in a cookie and in a header, and the
        // '+', '/' and '=' characters of plain base64 are percent-encoded in transit, so the
        // value that comes back would no longer hash to the value that was stored.
        var token = Base64UrlEncode(bytes);

        return (token, Hash(token), _clock.UtcNow.AddDays(_options.RefreshTokenDays));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
