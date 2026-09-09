using System.ComponentModel.DataAnnotations;

namespace MeetingRooms.Contracts.Bookings;

/// <summary>Request to book one slot on one date.</summary>
/// <param name="TimeSlotId">The slot to book.</param>
/// <param name="SlotDate">The date to book it for.</param>
public sealed record CreateBookingRequest(
    [property: Required] Guid TimeSlotId,
    [property: Required] DateOnly SlotDate);

/// <summary>
/// A booking, as returned from the API and broadcast over SignalR.
/// </summary>
/// <remarks>
/// The same shape serves both so that a client applying a live update and a client loading a
/// schedule are working with identical data, and the two paths cannot drift apart.
/// </remarks>
/// <param name="Id">Booking identifier.</param>
/// <param name="RoomId">Room the slot belongs to.</param>
/// <param name="RoomName">Room display name.</param>
/// <param name="TimeSlotId">The booked slot.</param>
/// <param name="SlotDate">The booked date.</param>
/// <param name="StartTime">Local start time of the slot.</param>
/// <param name="EndTime">Local end time of the slot.</param>
/// <param name="UserId">Owner of the booking.</param>
/// <param name="UserDisplayName">Owner's display name.</param>
/// <param name="CreatedUtc">When the booking was made.</param>
public sealed record BookingResponse(
    Guid Id,
    Guid RoomId,
    string RoomName,
    Guid TimeSlotId,
    DateOnly SlotDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    Guid UserId,
    string UserDisplayName,
    DateTimeOffset CreatedUtc);
