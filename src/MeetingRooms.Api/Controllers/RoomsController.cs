using MeetingRooms.Api.Auth;
using MeetingRooms.Api.Infrastructure;
using MeetingRooms.Contracts.Rooms;
using MeetingRooms.Core.Abstractions;
using MeetingRooms.Core.Entities;
using MeetingRooms.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api.Controllers;

/// <summary>Reading rooms and schedules, and administering the room catalogue.</summary>
/// <param name="roomService">Room and schedule operations.</param>
/// <param name="clock">Supplies the date a schedule defaults to.</param>
[ApiController]
[Route("api/rooms")]
[Authorize]
[Tags("Rooms")]
public sealed class RoomsController(IRoomService roomService, ScheduleClock clock) : ControllerBase
{
    /// <summary>
    /// Route name for the single-room endpoint.
    /// </summary>
    /// <remarks>
    /// A named route rather than <c>CreatedAtAction(nameof(GetRoomAsync))</c>: ASP.NET Core strips
    /// the <c>Async</c> suffix from action names by default, so the <c>nameof</c> would not match
    /// and URL generation would throw — turning a successful creation into a 500.
    /// </remarks>
    private const string GetRoomRouteName = "GetRoom";

    private readonly IRoomService roomService = roomService;
    private readonly ScheduleClock clock = clock;

    /// <summary>Lists rooms.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The rooms visible to the caller.</returns>
    [HttpGet]
    [EndpointSummary("List rooms")]
    [EndpointDescription("Administrators also see retired rooms; ordinary users see only active ones.")]
    [ProducesResponseType<IReadOnlyList<RoomResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<RoomResponse>>> GetRoomsAsync(
        CancellationToken cancellationToken)
    {
        var rooms = await this.roomService.GetRoomsAsync(
            includeInactive: this.User.IsAdmin(),
            cancellationToken);

        return this.Ok(rooms);
    }

    /// <summary>Reads one room.</summary>
    /// <param name="roomId">The room to read.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The room.</returns>
    [HttpGet("{roomId:guid}", Name = GetRoomRouteName)]
    [EndpointSummary("Get room")]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomResponse>> GetRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken)
    {
        var result = await this.roomService.GetRoomAsync(roomId, cancellationToken);

        return result.IsSuccess ? this.Ok(result.Value) : result.ToErrorResult(this);
    }

    /// <summary>Reads a room's schedule for a date.</summary>
    /// <param name="roomId">The room to read.</param>
    /// <param name="date">The date, defaulting to today when omitted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Every slot for that date, marked free or booked.</returns>
    [HttpGet("{roomId:guid}/schedule")]
    [EndpointSummary("Get schedule")]
    [EndpointDescription("Every slot in the room's template for one date, marked free or booked.")]
    [ProducesResponseType<ScheduleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScheduleResponse>> GetScheduleAsync(
        Guid roomId,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        // Through the clock rather than DateTime.UtcNow, so the default day is the one the booking
        // rules will judge the request against -- and so tests can choose it.
        var result = await this.roomService.GetScheduleAsync(
            roomId,
            date ?? this.clock.Today(),
            cancellationToken);

        return result.IsSuccess ? this.Ok(result.Value) : result.ToErrorResult(this);
    }

    /// <summary>Creates a room and its daily slot template.</summary>
    /// <param name="request">The room to create.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created room.</returns>
    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    [EndpointSummary("Create room")]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoomResponse>> CreateRoomAsync(
        [FromBody] CreateRoomRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await this.roomService.CreateRoomAsync(request, cancellationToken);

        return result.IsSuccess && result.Value is not null
            ? this.CreatedAtRoute(GetRoomRouteName, new { roomId = result.Value.Id }, result.Value)
            : result.ToErrorResult(this);
    }

    /// <summary>Updates a room's details.</summary>
    /// <param name="roomId">The room to update.</param>
    /// <param name="request">The new details.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The updated room.</returns>
    [HttpPut("{roomId:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [EndpointSummary("Update room")]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoomResponse>> UpdateRoomAsync(
        Guid roomId,
        [FromBody] UpdateRoomRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await this.roomService.UpdateRoomAsync(roomId, request, cancellationToken);

        return result.IsSuccess ? this.Ok(result.Value) : result.ToErrorResult(this);
    }

    /// <summary>Removes or retires a room.</summary>
    /// <param name="roomId">The room to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>No content.</returns>
    [HttpDelete("{roomId:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [EndpointSummary("Delete room")]
    [EndpointDescription("A room that has ever been booked is retired rather than deleted, so booking history survives.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRoomAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var result = await this.roomService.DeleteRoomAsync(roomId, cancellationToken);

        return result.IsSuccess ? this.NoContent() : result.ToErrorResult(this);
    }
}
