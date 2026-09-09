using MeetingRooms.Contracts.Bookings;
using MeetingRooms.Core.Results;

namespace MeetingRooms.Core.Abstractions;

/// <summary>
/// Creates and cancels bookings.
/// </summary>
/// <remarks>
/// Every write to the <c>Bookings</c> table goes through this service, so the conflict handling that
/// makes double-booking impossible lives in exactly one place. See <c>docs/concurrency.md</c>.
/// </remarks>
public interface IBookingService
{
    /// <summary>
    /// Books a slot for a date on behalf of a user.
    /// </summary>
    /// <param name="timeSlotId">The slot template to book.</param>
    /// <param name="slotDate">The calendar date to book it for.</param>
    /// <param name="userId">The user making the booking.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// <see cref="OperationOutcome.Success"/> for the single request that wins the slot, and
    /// <see cref="OperationOutcome.Conflict"/> for every other request — never an exception.
    /// </returns>
    Task<OperationResult<BookingResponse>> BookAsync(
        Guid timeSlotId,
        DateOnly slotDate,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a booking, releasing the slot for somebody else.
    /// </summary>
    /// <param name="bookingId">The booking to cancel.</param>
    /// <param name="requestingUserId">The user asking for the cancellation.</param>
    /// <param name="isAdmin">Whether that user may cancel bookings they do not own.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The cancelled booking on success, so it can be broadcast to schedule viewers.</returns>
    Task<OperationResult<BookingResponse>> CancelAsync(
        Guid bookingId,
        Guid requestingUserId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a user's own active bookings, soonest first.</summary>
    /// <param name="userId">The user whose bookings to list.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user's active bookings.</returns>
    Task<IReadOnlyList<BookingResponse>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists active bookings across every user. Administrators only.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>All active bookings, soonest first.</returns>
    Task<IReadOnlyList<BookingResponse>> GetAllAsync(CancellationToken cancellationToken = default);
}
