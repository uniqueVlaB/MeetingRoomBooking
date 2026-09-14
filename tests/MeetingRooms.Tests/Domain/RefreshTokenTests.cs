using MeetingRooms.Core.Entities;
using Xunit;

namespace MeetingRooms.Tests.Domain;

/// <summary>
/// When a refresh token may still be redeemed.
/// </summary>
/// <remarks>
/// This single predicate decides how long a stolen cookie stays useful, so its boundaries are
/// pinned here rather than inferred from the endpoint tests. Those prove the rule is applied; these
/// prove it is the right rule.
/// </remarks>
public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A token issued for the future and never revoked is redeemable.</summary>
    [Fact]
    public void AFreshToken_IsRedeemable()
    {
        var token = NewToken(expiresUtc: Now.AddDays(7));

        Assert.True(token.IsRedeemableAt(Now));
    }

    /// <summary>
    /// A token is dead at the instant it expires, not a moment later.
    /// </summary>
    /// <remarks>
    /// The difference between a strict and a non-strict comparison. It only shows up exactly on the
    /// boundary, which is precisely where a real clock will land eventually.
    /// </remarks>
    [Fact]
    public void ATokenAtItsExpiryInstant_IsNotRedeemable()
    {
        var token = NewToken(expiresUtc: Now);

        Assert.False(token.IsRedeemableAt(Now));
    }

    /// <summary>An expired token is not redeemable.</summary>
    [Fact]
    public void AnExpiredToken_IsNotRedeemable()
    {
        var token = NewToken(expiresUtc: Now.AddSeconds(-1));

        Assert.False(token.IsRedeemableAt(Now));
    }

    /// <summary>
    /// Revocation beats an unexpired lifetime.
    /// </summary>
    /// <remarks>
    /// This is what makes both signing out and rotation work: the replaced token still has days of
    /// life left on it, and must be refused anyway.
    /// </remarks>
    [Fact]
    public void ARevokedToken_IsNotRedeemableEvenWhileUnexpired()
    {
        var token = NewToken(expiresUtc: Now.AddDays(7));
        token.RevokedUtc = Now.AddMinutes(-1);

        Assert.False(token.IsRedeemableAt(Now));
    }

    /// <summary>Builds a token with the given expiry.</summary>
    /// <param name="expiresUtc">When the token stops being redeemable.</param>
    /// <returns>The token.</returns>
    private static RefreshToken NewToken(DateTimeOffset expiresUtc) => new()
    {
        TokenHash = "not-a-real-hash",
        UserId = Guid.CreateVersion7(),
        CreatedUtc = Now.AddDays(-1),
        ExpiresUtc = expiresUtc,
    };
}
