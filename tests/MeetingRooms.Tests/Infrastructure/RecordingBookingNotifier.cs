using MeetingRooms.Api.Realtime;
using MeetingRooms.Contracts.Bookings;

namespace MeetingRooms.Tests.Infrastructure;

/// <summary>Captures what the API would have broadcast, instead of sending it.</summary>
/// <remarks>
/// The live-update requirement is that viewers of a schedule are told about a change — and told
/// only <em>after</em> it has committed. Substituting the notifier is what lets a test assert both
/// without standing up a SignalR backplane and subscribing a real client to it.
/// </remarks>
public sealed class RecordingBookingNotifier : IBookingNotifier
{
    private readonly List<(string Method, BookingResponse Booking)> broadcasts = [];

    /// <summary>Every broadcast the API made, in order.</summary>
    public IReadOnlyList<(string Method, BookingResponse Booking)> Broadcasts
    {
        get
        {
            lock (this.broadcasts)
            {
                return [.. this.broadcasts];
            }
        }
    }

    /// <inheritdoc />
    public Task SlotBookedAsync(BookingResponse booking, CancellationToken cancellationToken = default)
    {
        this.Record(nameof(this.SlotBookedAsync), booking);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SlotReleasedAsync(BookingResponse booking, CancellationToken cancellationToken = default)
    {
        this.Record(nameof(this.SlotReleasedAsync), booking);

        return Task.CompletedTask;
    }

    /// <summary>Records one broadcast.</summary>
    /// <param name="method">Which notification was sent.</param>
    /// <param name="booking">The booking it carried.</param>
    private void Record(string method, BookingResponse booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        lock (this.broadcasts)
        {
            this.broadcasts.Add((method, booking));
        }
    }
}
