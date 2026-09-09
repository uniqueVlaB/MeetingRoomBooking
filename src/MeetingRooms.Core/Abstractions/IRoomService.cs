using MeetingRooms.Contracts.Rooms;
using MeetingRooms.Core.Results;

namespace MeetingRooms.Core.Abstractions;

/// <summary>
/// Reads rooms and their schedules, and lets administrators manage the room catalogue.
/// </summary>
public interface IRoomService
{
    /// <summary>Lists rooms and their slot templates.</summary>
    /// <param name="includeInactive">
    /// Whether retired rooms are included. Administrators need them; ordinary users do not.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The matching rooms, ordered by name.</returns>
    Task<IReadOnlyList<RoomResponse>> GetRoomsAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one room.</summary>
    /// <param name="roomId">The room to read.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The room, or a not-found result.</returns>
    Task<OperationResult<RoomResponse>> GetRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a room's schedule for one date: every slot, marked free or booked.
    /// </summary>
    /// <param name="roomId">The room to read.</param>
    /// <param name="date">The date to describe.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The schedule, or a not-found result when the room does not exist.</returns>
    Task<OperationResult<ScheduleResponse>> GetScheduleAsync(
        Guid roomId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a room and its slot template.</summary>
    /// <param name="request">The room to create.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created room, or a conflict when the name is taken.</returns>
    Task<OperationResult<RoomResponse>> CreateRoomAsync(
        CreateRoomRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a room's details.</summary>
    /// <param name="roomId">The room to update.</param>
    /// <param name="request">The new details.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The updated room, or a not-found or conflict result.</returns>
    Task<OperationResult<RoomResponse>> UpdateRoomAsync(
        Guid roomId,
        UpdateRoomRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a room so it accepts no further bookings.
    /// </summary>
    /// <remarks>
    /// A room that has ever been booked is deactivated rather than deleted, because deleting it
    /// would cascade its slots and take the booking history with them.
    /// </remarks>
    /// <param name="roomId">The room to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or a not-found result.</returns>
    Task<OperationResult<RoomResponse>> DeleteRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);
}
