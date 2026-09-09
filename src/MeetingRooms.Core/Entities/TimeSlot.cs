namespace MeetingRooms.Core.Entities;

/// <summary>
/// One entry in a room's fixed daily schedule, for example 09:00–10:00.
/// </summary>
/// <remarks>
/// A slot is a <em>template</em>, not a calendar event: it repeats every day and carries no date.
/// A <see cref="Booking"/> pairs a slot with a <see cref="Booking.SlotDate"/>. Storing the template
/// once — instead of materialising a row per room per day — keeps the table small and means adding
/// a room does not require back-filling a calendar.
/// </remarks>
public sealed class TimeSlot
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The room this slot belongs to.</summary>
    public Guid RoomId { get; set; }

    /// <summary>Navigation to the owning room.</summary>
    public Room? Room { get; set; }

    /// <summary>Local start time of day, e.g. 09:00.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Local end time of day, e.g. 10:00.</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>Position in the room's day, used for stable ordering in the UI.</summary>
    public int Ordinal { get; set; }

    /// <summary>Bookings made against this slot on specific dates.</summary>
    public ICollection<Booking> Bookings { get; set; } = [];
}
