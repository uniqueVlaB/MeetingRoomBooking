using System.ComponentModel.DataAnnotations;

namespace MeetingRooms.Contracts.Rooms;

/// <summary>One entry in a room's fixed daily schedule.</summary>
/// <param name="Id">Slot identifier, used when booking.</param>
/// <param name="StartTime">Local start time, e.g. 09:00.</param>
/// <param name="EndTime">Local end time, e.g. 10:00.</param>
/// <param name="Ordinal">Position in the room's day, for stable ordering.</param>
public sealed record TimeSlotResponse(Guid Id, TimeOnly StartTime, TimeOnly EndTime, int Ordinal);

/// <summary>A bookable room and its slot template.</summary>
/// <param name="Id">Room identifier.</param>
/// <param name="Name">Display name, unique among rooms.</param>
/// <param name="Location">Free-text location such as a floor or building.</param>
/// <param name="Capacity">How many people the room seats.</param>
/// <param name="IsActive">Whether the room currently accepts bookings.</param>
/// <param name="TimeSlots">The room's daily slots, ordered by <see cref="TimeSlotResponse.Ordinal"/>.</param>
public sealed record RoomResponse(
    Guid Id,
    string Name,
    string? Location,
    int Capacity,
    bool IsActive,
    IReadOnlyList<TimeSlotResponse> TimeSlots);

/// <summary>A slot to create as part of a room's template.</summary>
/// <param name="StartTime">Local start time.</param>
/// <param name="EndTime">Local end time, which must be after the start.</param>
public sealed record TimeSlotRequest(
    [Required] TimeOnly StartTime,
    [Required] TimeOnly EndTime);

/// <summary>Request to create a room together with its daily slots.</summary>
/// <param name="Name">Display name, unique among rooms.</param>
/// <param name="Location">Optional free-text location.</param>
/// <param name="Capacity">How many people the room seats.</param>
/// <param name="TimeSlots">The daily slot template. A room with no slots cannot be booked.</param>
public sealed record CreateRoomRequest(
    [Required, StringLength(128, MinimumLength = 1)] string Name,
    [StringLength(256)] string? Location,
    [Range(1, 1000)] int Capacity,
    [Required, MinLength(1)] IReadOnlyList<TimeSlotRequest> TimeSlots);

/// <summary>
/// Request to update a room's details.
/// </summary>
/// <remarks>
/// The slot template is deliberately not editable here. Changing slots under existing bookings
/// would orphan them, so slots are managed by recreating the room.
/// </remarks>
/// <param name="Name">Display name, unique among rooms.</param>
/// <param name="Location">Optional free-text location.</param>
/// <param name="Capacity">How many people the room seats.</param>
/// <param name="IsActive">Whether the room accepts new bookings.</param>
public sealed record UpdateRoomRequest(
    [Required, StringLength(128, MinimumLength = 1)] string Name,
    [StringLength(256)] string? Location,
    [Range(1, 1000)] int Capacity,
    bool IsActive);

/// <summary>One slot on one date, with its current booking state.</summary>
/// <param name="TimeSlotId">The slot being described.</param>
/// <param name="StartTime">Local start time.</param>
/// <param name="EndTime">Local end time.</param>
/// <param name="Ordinal">Position in the room's day.</param>
/// <param name="IsBooked">Whether an active booking holds this slot on this date.</param>
/// <param name="BookingId">The holding booking, when there is one.</param>
/// <param name="BookedByUserId">Who holds the slot, when it is booked.</param>
/// <param name="BookedByDisplayName">Display name of the holder, when it is booked.</param>
public sealed record ScheduleSlotResponse(
    Guid TimeSlotId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Ordinal,
    bool IsBooked,
    Guid? BookingId,
    Guid? BookedByUserId,
    string? BookedByDisplayName);

/// <summary>A room's schedule for a single date: which slots are free and which are booked.</summary>
/// <param name="RoomId">The room described.</param>
/// <param name="RoomName">The room's display name.</param>
/// <param name="Date">The date described.</param>
/// <param name="Slots">Every slot in the room's template, each marked free or booked.</param>
public sealed record ScheduleResponse(
    Guid RoomId,
    string RoomName,
    DateOnly Date,
    IReadOnlyList<ScheduleSlotResponse> Slots);
