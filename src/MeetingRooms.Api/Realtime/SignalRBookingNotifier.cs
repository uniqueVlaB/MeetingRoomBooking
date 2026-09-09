using MeetingRooms.Api.Hubs;
using MeetingRooms.Contracts.Bookings;
using Microsoft.AspNetCore.SignalR;

namespace MeetingRooms.Api.Realtime;

/// <summary>
/// Fans booking changes out over SignalR — backed by Azure SignalR Service when a connection string
/// is configured, and by the in-process hub otherwise.
/// </summary>
/// <param name="hubContext">Context for <see cref="BookingHub"/>.</param>
public sealed class SignalRBookingNotifier(IHubContext<BookingHub> hubContext) : IBookingNotifier
{
    private readonly IHubContext<BookingHub> hubContext = hubContext;

    /// <inheritdoc />
    public Task SlotBookedAsync(BookingResponse booking, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return this.hubContext.Clients
            .Group(BookingHub.GroupFor(booking.RoomId, booking.SlotDate))
            .SendAsync(BookingHub.SlotBookedMethod, booking, cancellationToken);
    }

    /// <inheritdoc />
    public Task SlotReleasedAsync(BookingResponse booking, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return this.hubContext.Clients
            .Group(BookingHub.GroupFor(booking.RoomId, booking.SlotDate))
            .SendAsync(BookingHub.SlotReleasedMethod, booking, cancellationToken);
    }
}
