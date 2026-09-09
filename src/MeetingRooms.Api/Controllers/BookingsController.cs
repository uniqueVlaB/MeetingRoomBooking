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
        await this.notifier.SlotBookedAsync(result.Value, cancellationToken);

        return this.CreatedAtAction(
            nameof(this.GetMyBookingsAsync),
            new { bookingId = result.Value.Id },
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

        await this.notifier.SlotReleasedAsync(result.Value, cancellationToken);

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
