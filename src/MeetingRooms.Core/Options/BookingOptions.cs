using System.ComponentModel.DataAnnotations;

namespace MeetingRooms.Core.Options;

/// <summary>Rules governing when a slot may be booked.</summary>
public sealed class BookingOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "Booking";

    /// <summary>
    /// The time zone the schedule is expressed in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="Entities.TimeSlot"/> carries a local wall-clock time and a
    /// <see cref="Entities.Booking"/> a local calendar date, so "is this date in the past?" is only
    /// answerable against a time zone. Comparing against the server's UTC date instead used to make
    /// the rule wrong in both directions: a user behind UTC was refused their own current day once
    /// UTC had rolled over, and a user ahead of it could book a day that had already ended.
    /// </para>
    /// <para>
    /// An IANA or Windows identifier — <c>Europe/Kyiv</c>, <c>UTC</c>. The system runs on one shared
    /// office calendar, so this is a single setting rather than a per-room column; if rooms in
    /// different regions are ever needed, this is the value that moves onto the room.
    /// </para>
    /// </remarks>
    [Required]
    public string TimeZone { get; set; } = "UTC";

    /// <summary>
    /// How far ahead a slot may be booked, in days.
    /// </summary>
    /// <remarks>
    /// A guard against absurd input rather than a business rule; without it a typo in the year would
    /// silently create a booking nobody will ever see.
    /// </remarks>
    [Range(1, 3650)]
    public int MaxDaysAhead { get; set; } = 365;
}
