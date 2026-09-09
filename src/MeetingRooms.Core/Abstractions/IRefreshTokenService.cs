using MeetingRooms.Core.Results;

namespace MeetingRooms.Core.Abstractions;

/// <summary>A refresh token and the moment it stops working.</summary>
/// <param name="Token">The raw token. Stored only in the user's cookie; the database keeps a hash.</param>
/// <param name="ExpiresUtc">When the token stops being redeemable.</param>
public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresUtc);

/// <summary>The result of redeeming a refresh token: who it belongs to, and its replacement.</summary>
/// <param name="UserId">The user the redeemed token authenticated.</param>
/// <param name="Token">The replacement token to send back in the cookie.</param>
/// <param name="ExpiresUtc">When the replacement stops being redeemable.</param>
public sealed record RotatedRefreshToken(Guid UserId, string Token, DateTimeOffset ExpiresUtc);

/// <summary>
/// Issues, rotates and revokes the long-lived tokens that keep a user signed in.
/// </summary>
/// <remarks>
/// Tokens are rotated on every use: redeeming one revokes it and returns a replacement. That keeps
/// the window in which any single captured token is useful down to a single request, and it makes
/// replay detectable — a revoked token presented again means the cookie leaked.
/// </remarks>
public interface IRefreshTokenService
{
    /// <summary>Issues a new refresh token for a user.</summary>
    /// <param name="userId">The user to issue for.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The raw token and its expiry. The raw value is never stored.</returns>
    Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Redeems a token, revoking it and issuing a replacement.</summary>
    /// <param name="rawToken">The token from the caller's cookie.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The owning user and a replacement token, or
    /// <see cref="OperationOutcome.Forbidden"/> when the token is unknown, expired or already used.
    /// </returns>
    Task<OperationResult<RotatedRefreshToken>> RotateAsync(
        string rawToken,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a token, ending the session.</summary>
    /// <param name="rawToken">The token from the caller's cookie.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default);
}
