using MeetingRooms.Core.Entities;

namespace MeetingRooms.Core.Abstractions;

/// <summary>
/// The entity sets a service may touch, and the single point at which changes are committed.
/// </summary>
/// <remarks>
/// <see cref="SaveChangesAsync"/> deliberately does not swallow provider exceptions. The booking
/// flow needs to see the unique-index violation in order to translate it into a conflict, so
/// hiding it here would break the concurrency guarantee.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Bookable rooms.</summary>
    IRepository<Room> Rooms { get; }

    /// <summary>Daily slot templates owned by rooms.</summary>
    IRepository<TimeSlot> TimeSlots { get; }

    /// <summary>Bookings of a slot on a date.</summary>
    IRepository<Booking> Bookings { get; }

    /// <summary>Commits every staged change.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of rows affected.</returns>
    /// <exception cref="Exception">
    /// Provider exceptions propagate; callers that write bookings must ask
    /// <see cref="IDatabaseConflictDetector"/> whether the failure was a lost race.
    /// </exception>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
