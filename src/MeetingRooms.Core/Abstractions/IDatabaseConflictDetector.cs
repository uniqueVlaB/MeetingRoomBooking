namespace MeetingRooms.Core.Abstractions;

/// <summary>
/// Recognises the database errors that mean "somebody else got there first".
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsActiveSlotConflict"/> is the hinge of the concurrency design. Booking is a single
/// INSERT guarded by the filtered unique index <c>UX_Bookings_ActiveSlot</c>; the losing request
/// fails at the database, and this abstraction is what lets the service turn that specific failure
/// into <see cref="Results.OperationOutcome.Conflict"/> rather than letting it escape as a 500.
/// </para>
/// <para>
/// It is an interface, and lives in Core rather than beside its implementation, so the domain does
/// not depend on SQL Server error numbers. The implementation is
/// <c>SqlServerConflictDetector</c> in <c>MeetingRooms.Infrastructure.SQL</c>.
/// </para>
/// </remarks>
public interface IDatabaseConflictDetector
{
    /// <summary>
    /// Determines whether an exception from a save is the active-slot conflict — that is, a lost
    /// booking race.
    /// </summary>
    /// <param name="exception">The exception the save threw.</param>
    /// <returns>
    /// <see langword="true"/> only when the failure is a violation of the active-slot unique index.
    /// An unrelated constraint violation — a duplicate room name, say — must return
    /// <see langword="false"/>, so it is never reported to a user as "this slot is already booked".
    /// </returns>
    bool IsActiveSlotConflict(Exception exception);

    /// <summary>
    /// Determines whether an exception from a save is any unique-constraint violation.
    /// </summary>
    /// <remarks>
    /// Used where uniqueness is a validation concern rather than a concurrency guarantee — creating
    /// a room whose name is taken, for instance.
    /// </remarks>
    /// <param name="exception">The exception the save threw.</param>
    /// <returns><see langword="true"/> for any unique-index or unique-constraint violation.</returns>
    bool IsUniqueViolation(Exception exception);
}
