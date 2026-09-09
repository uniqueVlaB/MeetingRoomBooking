namespace MeetingRooms.Core.Entities;

/// <summary>
/// A user's claim on one <see cref="TimeSlot"/> for one calendar date.
/// </summary>
/// <remarks>
/// The no-double-booking rule is enforced by the filtered unique index
/// <c>UX_Bookings_ActiveSlot</c> over (<see cref="TimeSlotId"/>, <see cref="SlotDate"/>) where
/// <see cref="Status"/> is <see cref="BookingStatus.Active"/>. Creating a booking is therefore a
/// single INSERT with no preceding availability check. See <c>docs/concurrency.md</c>.
/// </remarks>
public sealed class Booking
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The slot template being booked.</summary>
    public Guid TimeSlotId { get; set; }

    /// <summary>Navigation to the slot.</summary>
    public TimeSlot? TimeSlot { get; set; }

    /// <summary>The calendar date the slot is booked for.</summary>
    public DateOnly SlotDate { get; set; }

    /// <summary>The user who owns the booking.</summary>
    public Guid UserId { get; set; }

    /// <summary>Navigation to the owning user.</summary>
    public ApplicationUser? User { get; set; }

    /// <summary>Whether the booking still holds the slot.</summary>
    public BookingStatus Status { get; set; } = BookingStatus.Active;

    /// <summary>When the booking was created (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the booking was cancelled (UTC), if it was.</summary>
    public DateTimeOffset? CancelledUtc { get; set; }

    /// <summary>
    /// SQL Server <c>rowversion</c>. Guards cancellation against a lost update when two clients act
    /// on the same booking; the unique index is what guards creation. The two are complementary:
    /// one protects updates, the other protects inserts.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}
