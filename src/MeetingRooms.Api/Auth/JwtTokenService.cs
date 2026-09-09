using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MeetingRooms.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MeetingRooms.Api.Auth;

/// <summary>Issues HMAC-SHA256 signed JWTs.</summary>
/// <param name="options">Issuer, audience, signing key and lifetime.</param>
/// <param name="timeProvider">Supplies the current time, so expiry is testable.</param>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JwtOptions options = options.Value;
    private readonly TimeProvider timeProvider = timeProvider;

    /// <inheritdoc />
    public AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);

        var issuedAt = this.timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(this.options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimNames.Subject, user.Id.ToString()),
            new(ClaimNames.Name, user.DisplayName),
            new(ClaimNames.Email, user.Email ?? string.Empty),

            // A unique token id, so an individual token can be identified in logs.
            new(ClaimNames.TokenId, Guid.CreateVersion7().ToString()),
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimNames.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this.options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: this.options.Issuer,
            audience: this.options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
