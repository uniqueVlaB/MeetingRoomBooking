using MeetingRooms.Core.Entities;

namespace MeetingRooms.Api.Auth;

/// <summary>A signed access token and its expiry.</summary>
/// <param name="Value">The encoded JWT.</param>
/// <param name="ExpiresUtc">When the token stops being accepted.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresUtc);

/// <summary>Issues access tokens.</summary>
/// <remarks>
/// An interface so the integration tests can mint tokens through exactly the same code path the API
/// uses at runtime, rather than hand-rolling a token that might validate differently.
/// </remarks>
public interface ITokenService
{
    /// <summary>Creates a signed access token for a user.</summary>
    /// <param name="user">The user to authenticate.</param>
    /// <param name="roles">Roles to embed as claims.</param>
    /// <returns>The encoded token and its expiry.</returns>
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
}
