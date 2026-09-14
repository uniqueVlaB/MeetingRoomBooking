using MeetingRooms.Api.Auth;
using MeetingRooms.Api.Infrastructure;
using MeetingRooms.Api.Realtime;
using MeetingRooms.Contracts.Bookings;
using MeetingRooms.Core.Abstractions;
using MeetingRooms.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api.Controllers;

/// <summary>Booking and cancelling slots.</summary>
/// <remarks>
/// <see cref="BookAsync"/> is the endpoint the concurrency requirement is about: when several
/// requests arrive for one slot at the same moment, exactly one receives <c>201</c> and the rest
/// receive <c>409</c>. Neither the controller nor the service decides that — the database does.
/// See <c>docs/concurrency.md</c>.
/// </remarks>
/// <param name="bookingService">Booking operations.</param>
/// <param name="notifier">Broadcasts changes to schedule viewers.</param>
[ApiController]
[Route("api/bookings")]
[Authorize]
[Tags("Bookings")]
public sealed class BookingsController(
    IBookingService bookingService,
    IBookingNotifier notifier) : ControllerBase
{
    private readonly IBookingService bookingService = bookingService;
    private readonly IBookingNotifier notifier = notifier;

    /// <summary>Books a slot for a date.</summary>
    /// <param name="request">The slot and date to book.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created booking, or 409 when another request won the slot.</returns>
    [HttpPost]
    [EndpointSummary("Book a slot")]
    [EndpointDescription(
        "Exactly one of several simultaneous requests for the same slot succeeds; the rest receive 409.")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingResponse>> BookAsync(
        [FromBody] CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await this.bookingService.BookAsync(
            request.TimeSlotId,
            request.SlotDate,
            this.User.GetUserId(),
            cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            return result.ToErrorResult(this);
        }

        // Broadcast only after the write has committed, so nobody is told a slot is taken by a
        // booking that did not actually happen.
        //
        // Deliberately not the request's token: the booking is already committed, so this work is no
        // longer the caller's to cancel. Passing it would mean a client that closed its tab between
        // the commit and the send left every other viewer looking at a stale schedule -- the exact
        // failure the live-update requirement is about.
        await this.notifier.SlotBookedAsync(result.Value, CancellationToken.None);

        // There is no "get one booking" endpoint, so Location points at the schedule this booking
        // changed, which is the resource a client actually wants to look at next.
        return this.Created(
            $"/api/rooms/{result.Value.RoomId}/schedule?date={result.Value.SlotDate:yyyy-MM-dd}",
            result.Value);
    }

    /// <summary>Cancels a booking, releasing the slot.</summary>
    /// <param name="bookingId">The booking to cancel.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>No content.</returns>
    [HttpDelete("{bookingId:guid}")]
    [EndpointSummary("Cancel a booking")]
    [EndpointDescription("Owners can cancel their own bookings; administrators can cancel any.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await this.bookingService.CancelAsync(
            bookingId,
            this.User.GetUserId(),
            this.User.IsAdmin(),
            cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            return result.ToErrorResult(this);
        }

        // Not the request's token, for the same reason as in BookAsync: the cancellation has
        // committed, and other viewers must hear about it even if this caller has gone away.
        await this.notifier.SlotReleasedAsync(result.Value, CancellationToken.None);

        return this.NoContent();
    }

    /// <summary>Lists the caller's own active bookings.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The caller's bookings, soonest first.</returns>
    [HttpGet("mine")]
    [EndpointSummary("My bookings")]
    [ProducesResponseType<IReadOnlyList<BookingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<BookingResponse>>> GetMyBookingsAsync(
        CancellationToken cancellationToken) =>
        this.Ok(await this.bookingService.GetForUserAsync(this.User.GetUserId(), cancellationToken));

    /// <summary>Lists active bookings across every user.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>All bookings, soonest first.</returns>
    [HttpGet]
    [Authorize(Roles = RoleNames.Admin)]
    [EndpointSummary("All bookings")]
    [EndpointDescription("Administrators only.")]
    [ProducesResponseType<IReadOnlyList<BookingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<BookingResponse>>> GetAllBookingsAsync(
        CancellationToken cancellationToken) =>
        this.Ok(await this.bookingService.GetAllAsync(cancellationToken));
}
