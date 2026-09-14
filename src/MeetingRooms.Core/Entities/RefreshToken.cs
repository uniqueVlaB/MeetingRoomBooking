namespace MeetingRooms.Core.Entities;

/// <summary>
/// A long-lived token that buys a new access token without re-entering a password.
/// </summary>
/// <remarks>
/// <para>
/// Only a SHA-256 hash of the token is stored. The raw value exists in the user's HttpOnly cookie
/// and nowhere else, so a leaked database backup does not hand over live sessions.
/// </para>
/// <para>
/// Tokens are <em>rotated</em>: redeeming one revokes it and issues a replacement. A revoked token
/// arriving again means the cookie was captured and replayed, which is detectable precisely because
/// the used row is kept rather than deleted.
/// </para>
/// </remarks>
public sealed class RefreshToken
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>SHA-256 hash of the token value, hex-encoded. Unique across all tokens.</summary>
    public required string TokenHash { get; set; }

    /// <summary>The user the token authenticates.</summary>
    public Guid UserId { get; set; }

    /// <summary>Navigation to the owning user.</summary>
    public ApplicationUser? User { get; set; }

    /// <summary>When the token was issued (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the token stops being redeemable (UTC).</summary>
    public DateTimeOffset ExpiresUtc { get; set; }

    /// <summary>When the token was revoked, either by rotation or by signing out.</summary>
    public DateTimeOffset? RevokedUtc { get; set; }

    /// <summary>
    /// SQL Server <c>rowversion</c>. Guards redemption against two requests rotating the same token
    /// at once.
    /// </summary>
    /// <remarks>
    /// Redeeming is a read-then-write — check the token is live, then revoke it — so without this
    /// two concurrent refreshes presenting the same cookie would both pass the check and both issue
    /// a replacement, turning one captured token into two live sessions and defeating the replay
    /// detection described above. The rowversion makes the second writer fail instead, which is the
    /// same complementary pairing the <see cref="Booking"/> entity describes: the unique index
    /// guards inserts, the rowversion guards updates.
    /// </remarks>
    public byte[]? RowVersion { get; set; }

    /// <summary>Whether the token may still be redeemed at the given moment.</summary>
    /// <param name="now">The current time, passed in so the check is testable.</param>
    /// <returns><see langword="true"/> if the token is neither revoked nor expired.</returns>
    public bool IsRedeemableAt(DateTimeOffset now) => this.RevokedUtc is null && this.ExpiresUtc > now;
}
