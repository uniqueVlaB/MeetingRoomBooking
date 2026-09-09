namespace MeetingRooms.Core.Entities;

/// <summary>
/// A bookable resource — a meeting room. A room owns a fixed set of daily
/// <see cref="TimeSlot"/> templates; bookings attach a slot to a calendar date.
/// </summary>
public sealed class Room
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Display name, unique among rooms (e.g. "Kyiv — Focus Room").</summary>
    public required string Name { get; set; }

    /// <summary>Free-text location, e.g. floor or building.</summary>
    public string? Location { get; set; }

    /// <summary>Number of people the room seats.</summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Rooms are retired by clearing this flag rather than being deleted, so historical bookings
    /// keep a valid foreign key.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>SQL Server <c>rowversion</c>, used for optimistic concurrency on admin edits.</summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>The room's fixed daily slot template, ordered by <see cref="TimeSlot.Ordinal"/>.</summary>
    public ICollection<TimeSlot> TimeSlots { get; set; } = [];
}
