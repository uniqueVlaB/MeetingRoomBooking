namespace MeetingRooms.Core.Abstractions;

/// <summary>
/// Recognises the database error produced when a booking loses the race for a slot.
/// </summary>
/// <remarks>
/// <para>
/// This is the hinge of the concurrency design. Booking is a single INSERT guarded by the filtered
/// unique index <c>UX_Bookings_ActiveSlot</c>; the losing request fails at the database, and this
/// abstraction is what lets the service turn that specific failure into
/// <see cref="Results.OperationOutcome.Conflict"/> instead of letting it escape as a server error.
/// </para>
/// <para>
/// It is an interface, and lives in Core rather than the implementation, so that the domain does not
/// depend on SQL Server error numbers. The SQL Server implementation is
/// <c>SqlServerSlotConflictDetector</c> in <c>MeetingRooms.Infrastructure.SQL</c>.
/// </para>
/// </remarks>
public interface ISlotConflictDetector
{
    /// <summary>
    /// Determines whether an exception from <c>SaveChangesAsync</c> is the active-slot conflict.
    /// </summary>
    /// <param name="exception">The exception the save threw.</param>
    /// <returns>
    /// <see langword="true"/> only when the failure is a violation of the active-slot unique index.
    /// An unrelated constraint violation — a duplicate room name, say — must return
    /// <see langword="false"/> so it is not silently reported as a booking conflict.
    /// </returns>
    bool IsActiveSlotConflict(Exception exception);
}
