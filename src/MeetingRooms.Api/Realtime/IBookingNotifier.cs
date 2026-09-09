using MeetingRooms.Contracts.Bookings;

namespace MeetingRooms.Api.Realtime;

/// <summary>
/// Broadcasts booking changes to connected schedule viewers.
/// </summary>
/// <remarks>
/// An interface rather than a direct <c>IHubContext</c> dependency, so booking flows can be tested
/// without a SignalR backplane and so the transport can change without touching callers.
/// </remarks>
public interface IBookingNotifier
{
    /// <summary>Announces that a slot has just been taken.</summary>
    /// <param name="booking">The booking that now holds the slot.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the message has been handed to the transport.</returns>
    Task SlotBookedAsync(BookingResponse booking, CancellationToken cancellationToken = default);

    /// <summary>Announces that a slot has been released by a cancellation.</summary>
    /// <param name="booking">The booking that was cancelled.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the message has been handed to the transport.</returns>
    Task SlotReleasedAsync(BookingResponse booking, CancellationToken cancellationToken = default);
}
