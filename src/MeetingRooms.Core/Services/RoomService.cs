using FluentValidation;
using MeetingRooms.Contracts.Rooms;
using MeetingRooms.Core.Abstractions;
using MeetingRooms.Core.Entities;
using MeetingRooms.Core.Results;
using Microsoft.EntityFrameworkCore;

namespace MeetingRooms.Core.Services;

/// <summary>
/// Reads rooms and schedules, and lets administrators manage the room catalogue.
/// </summary>
/// <param name="unitOfWork">Entity access and the commit point.</param>
/// <param name="conflictDetector">Recognises a duplicate room name in a failed save.</param>
/// <param name="createRoomValidator">Checks the cross-field rules on a new room's slots.</param>
public sealed class RoomService(
    IUnitOfWork unitOfWork,
    IDatabaseConflictDetector conflictDetector,
    IValidator<CreateRoomRequest> createRoomValidator) : IRoomService
{
    private readonly IUnitOfWork unitOfWork = unitOfWork;
    private readonly IDatabaseConflictDetector conflictDetector = conflictDetector;
    private readonly IValidator<CreateRoomRequest> createRoomValidator = createRoomValidator;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoomResponse>> GetRoomsAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = this.unitOfWork.Rooms.QueryAsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(room => room.IsActive);
        }

        // Materialise first: Project is ordinary C# and has no SQL translation, and the slot list it
        // reads has to be loaded anyway.
        var rooms = await query
            .Include(room => room.TimeSlots)
            .OrderBy(room => room.Name)
            .ToListAsync(cancellationToken);

        return rooms.Select(Project).ToList();
    }

    /// <inheritdoc />
    public async Task<OperationResult<RoomResponse>> GetRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var room = await this.unitOfWork.Rooms
            .QueryAsNoTracking()
            .Include(candidate => candidate.TimeSlots)
            .FirstOrDefaultAsync(candidate => candidate.Id == roomId, cancellationToken);

        return room is null
            ? OperationResult<RoomResponse>.NotFound($"Room '{roomId}' does not exist.")
            : OperationResult<RoomResponse>.Success(Project(room));
    }

    /// <inheritdoc />
    public async Task<OperationResult<ScheduleResponse>> GetScheduleAsync(
        Guid roomId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var room = await this.unitOfWork.Rooms
            .QueryAsNoTracking()
            .Include(candidate => candidate.TimeSlots)
            .FirstOrDefaultAsync(candidate => candidate.Id == roomId, cancellationToken);

        if (room is null)
        {
            return OperationResult<ScheduleResponse>.NotFound($"Room '{roomId}' does not exist.");
        }

        // Only active bookings occupy a slot; cancelled rows are history and leave the slot free,
        // exactly as the unique index's filter does.
        var held = await this.unitOfWork.Bookings
            .QueryAsNoTracking()
            .Where(booking => booking.SlotDate == date
                && booking.Status == BookingStatus.Active
                && booking.TimeSlot!.RoomId == roomId)
            .Select(booking => new
            {
                booking.Id,
                booking.TimeSlotId,
                booking.UserId,
                DisplayName = booking.User!.DisplayName,
            })
            .ToDictionaryAsync(booking => booking.TimeSlotId, cancellationToken);

        var slots = room.TimeSlots
            .OrderBy(slot => slot.Ordinal)
            .ThenBy(slot => slot.StartTime)
            .Select(slot => held.TryGetValue(slot.Id, out var booking)
                ? new ScheduleSlotResponse(
                    slot.Id,
                    slot.StartTime,
                    slot.EndTime,
                    slot.Ordinal,
                    IsBooked: true,
                    booking.Id,
                    booking.UserId,
                    booking.DisplayName)
                : new ScheduleSlotResponse(
                    slot.Id,
                    slot.StartTime,
                    slot.EndTime,
                    slot.Ordinal,
                    IsBooked: false,
                    BookingId: null,
                    BookedByUserId: null,
                    BookedByDisplayName: null))
            .ToList();

        return OperationResult<ScheduleResponse>.Success(
            new ScheduleResponse(room.Id, room.Name, date, slots));
    }

    /// <inheritdoc />
    public async Task<OperationResult<RoomResponse>> CreateRoomAsync(
        CreateRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await this.createRoomValidator.ValidateAsync(request, cancellationToken);

        if (!validation.IsValid)
        {
            return OperationResult<RoomResponse>.Invalid(
                string.Join(" ", validation.Errors.Select(error => error.ErrorMessage)));
        }

        var room = new Room
        {
            Name = request.Name.Trim(),
            Location = request.Location?.Trim(),
            Capacity = request.Capacity,
            IsActive = true,
        };

        // Ordinal is assigned from the sorted order rather than trusted from the request, so the
        // schedule always renders chronologically regardless of how the slots were submitted.
        var ordinal = 0;

        foreach (var slot in request.TimeSlots.OrderBy(slot => slot.StartTime))
        {
            room.TimeSlots.Add(new TimeSlot
            {
                RoomId = room.Id,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                Ordinal = ordinal++,
            });
        }

        this.unitOfWork.Rooms.Add(room);

        try
        {
            await this.unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (this.conflictDetector.IsUniqueViolation(exception))
        {
            return OperationResult<RoomResponse>.Conflict($"A room named '{room.Name}' already exists.");
        }

        return OperationResult<RoomResponse>.Success(Project(room));
    }

    /// <inheritdoc />
    public async Task<OperationResult<RoomResponse>> UpdateRoomAsync(
        Guid roomId,
        UpdateRoomRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var room = await this.unitOfWork.Rooms
            .Query()
            .Include(candidate => candidate.TimeSlots)
            .FirstOrDefaultAsync(candidate => candidate.Id == roomId, cancellationToken);

        if (room is null)
        {
            return OperationResult<RoomResponse>.NotFound($"Room '{roomId}' does not exist.");
        }

        room.Name = request.Name.Trim();
        room.Location = request.Location?.Trim();
        room.Capacity = request.Capacity;
        room.IsActive = request.IsActive;

        try
        {
            await this.unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (this.conflictDetector.IsUniqueViolation(exception))
        {
            return OperationResult<RoomResponse>.Conflict($"A room named '{room.Name}' already exists.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult<RoomResponse>.Conflict(
                "That room was changed by somebody else. Reload and try again.");
        }

        return OperationResult<RoomResponse>.Success(Project(room));
    }

    /// <inheritdoc />
    public async Task<OperationResult<RoomResponse>> DeleteRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var room = await this.unitOfWork.Rooms
            .Query()
            .Include(candidate => candidate.TimeSlots)
            .FirstOrDefaultAsync(candidate => candidate.Id == roomId, cancellationToken);

        if (room is null)
        {
            return OperationResult<RoomResponse>.NotFound($"Room '{roomId}' does not exist.");
        }

        // Retiring first is what makes the decision below safe. Between reading the booking count
        // and acting on it there is a window in which somebody books the room, and deleting it then
        // would cascade the slots and silently take that booking with them. Clearing IsActive closes
        // the window: BookAsync refuses an inactive room, so no booking can appear after this save.
        room.IsActive = false;

        try
        {
            await this.unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult<RoomResponse>.Conflict(
                "That room was changed by somebody else. Reload and try again.");
        }

        var hasBookings = await this.unitOfWork.Bookings
            .QueryAsNoTracking()
            .AnyAsync(booking => booking.TimeSlot!.RoomId == roomId, cancellationToken);

        if (hasBookings)
        {
            // A room that has ever been booked stays as a retired row, so the catalogue remains
            // honest and past bookings keep a valid foreign key.
            return OperationResult<RoomResponse>.Success(Project(room));
        }

        this.unitOfWork.Rooms.Remove(room);

        try
        {
            await this.unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody edited the room between the two saves. It is already retired, which is the
            // outcome that matters; leaving the row behind is harmless.
            return OperationResult<RoomResponse>.Success(Project(room));
        }

        return OperationResult<RoomResponse>.Success(Project(room));
    }

    /// <summary>Maps a room and its slots onto the contract shape.</summary>
    /// <param name="room">The room to project.</param>
    /// <returns>The room in its contract shape.</returns>
    private static RoomResponse Project(Room room) => new(
        room.Id,
        room.Name,
        room.Location,
        room.Capacity,
        room.IsActive,
        room.TimeSlots
            .OrderBy(slot => slot.Ordinal)
            .ThenBy(slot => slot.StartTime)
            .Select(slot => new TimeSlotResponse(slot.Id, slot.StartTime, slot.EndTime, slot.Ordinal))
            .ToList());
}
