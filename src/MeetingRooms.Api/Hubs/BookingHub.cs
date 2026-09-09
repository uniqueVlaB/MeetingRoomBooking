using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MeetingRooms.Api.Hubs;

/// <summary>
/// Pushes booking changes to everyone currently looking at a room's schedule.
/// </summary>
/// <remarks>
/// Clients subscribe per room <em>and</em> date, so a browser showing Monday is not woken by a
/// change to Tuesday. Groups are the unit of fan-out that Azure SignalR scales on our behalf; the
/// server never tracks connections itself, which is what lets the API run on more than one instance
/// without any shared state of its own.
/// </remarks>
[Authorize]
public sealed class BookingHub : Hub
{
    /// <summary>The path the hub is mapped to. Shared with the client configuration.</summary>
    public const string Route = "/hubs/bookings";

    /// <summary>Server-to-client method invoked when a slot becomes booked.</summary>
    public const string SlotBookedMethod = "SlotBooked";

    /// <summary>Server-to-client method invoked when a cancellation releases a slot.</summary>
    public const string SlotReleasedMethod = "SlotReleased";

    /// <summary>
    /// Builds the group name for a room and date.
    /// </summary>
    /// <remarks>
    /// Used by both the hub and the notifier, so the subscriber and the publisher cannot disagree
    /// about the name — a mismatch would not error, it would simply deliver nothing.
    /// </remarks>
    /// <param name="roomId">The room being watched.</param>
    /// <param name="date">The date being watched.</param>
    /// <returns>The SignalR group name.</returns>
    public static string GroupFor(Guid roomId, DateOnly date) => $"room:{roomId:N}:{date:yyyy-MM-dd}";

    /// <summary>Subscribes the caller to updates for one room on one date.</summary>
    /// <param name="roomId">The room to watch.</param>
    /// <param name="date">The date to watch, as <c>yyyy-MM-dd</c>.</param>
    /// <returns>A task that completes when the caller has joined.</returns>
    public Task JoinRoomAsync(Guid roomId, DateOnly date) =>
        this.Groups.AddToGroupAsync(this.Context.ConnectionId, GroupFor(roomId, date));

    /// <summary>Unsubscribes the caller, for example when they switch room or date.</summary>
    /// <param name="roomId">The room to stop watching.</param>
    /// <param name="date">The date to stop watching.</param>
    /// <returns>A task that completes when the caller has left.</returns>
    public Task LeaveRoomAsync(Guid roomId, DateOnly date) =>
        this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, GroupFor(roomId, date));
}
