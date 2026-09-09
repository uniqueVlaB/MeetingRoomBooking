namespace MeetingRooms.Core.Entities;

/// <summary>
/// The two roles in the system.
/// </summary>
/// <remarks>
/// Constants rather than string literals so the seeder, the <c>[Authorize(Roles = ...)]</c>
/// attributes, the token claims and the tests cannot disagree about spelling — a mismatch there
/// fails open into "no access" and is tedious to diagnose.
/// </remarks>
public static class RoleNames
{
    /// <summary>Can view resources and their schedules, and book available slots.</summary>
    public const string User = "User";

    /// <summary>Can additionally manage rooms and view all bookings across users.</summary>
    public const string Admin = "Admin";

    /// <summary>Every role, for seeding.</summary>
    public static IReadOnlyList<string> All { get; } = [User, Admin];
}
