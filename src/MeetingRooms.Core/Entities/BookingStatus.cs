namespace MeetingRooms.Core.Entities;

/// <summary>
/// Lifecycle of a booking.
/// </summary>
/// <remarks>
/// The numeric values are part of the database contract: the unique index that prevents
/// double-booking is filtered on <c>[Status] = 0</c>. Do not renumber these members without writing
/// a migration that rebuilds <c>UX_Bookings_ActiveSlot</c> — doing so would silently disable the
/// guarantee rather than fail loudly. See <c>docs/concurrency.md</c>.
/// </remarks>
public enum BookingStatus
{
    /// <summary>The booking holds the slot. At most one active booking may exist per slot and date.</summary>
    Active = 0,

    /// <summary>
    /// The booking was cancelled. The row is kept for history and is excluded from the unique index,
    /// so the slot becomes bookable again.
    /// </summary>
    Cancelled = 1,
}
