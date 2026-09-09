using Microsoft.AspNetCore.Identity;

namespace MeetingRooms.Core.Entities;

/// <summary>
/// An account in the system.
/// </summary>
/// <remarks>
/// Keys are <see cref="Guid"/> rather than the Identity default of <see cref="string"/>, so user
/// identifiers are the same shape as every other identifier in the domain and can be compared
/// without parsing.
/// </remarks>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Name shown beside this user's bookings on a schedule.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Bookings this user holds or has held.</summary>
    public ICollection<Booking> Bookings { get; set; } = [];

    /// <summary>Refresh tokens issued to this user.</summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
