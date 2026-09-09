using System.ComponentModel.DataAnnotations;

namespace MeetingRooms.Core.Options;

/// <summary>How long a refresh token stays redeemable.</summary>
public sealed class RefreshTokenOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "RefreshToken";

    /// <summary>
    /// Lifetime in days.
    /// </summary>
    /// <remarks>
    /// This is how long a user stays signed in without re-entering a password, so it trades
    /// convenience against the window in which a stolen cookie is useful. A week is the default.
    /// </remarks>
    [Range(1, 90)]
    public int LifetimeDays { get; set; } = 7;
}
