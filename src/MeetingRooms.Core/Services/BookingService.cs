using System.Linq.Expressions;
using MeetingRooms.Contracts.Bookings;
using MeetingRooms.Core.Abstractions;
using MeetingRooms.Core.Entities;
using MeetingRooms.Core.Options;
using MeetingRooms.Core.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingRooms.Core.Services;

/// <summary>
/// Creates and cancels bookings.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Read <c>docs/concurrency.md</c> before changing <see cref="BookAsync"/>.</strong> Booking
/// is a single INSERT whose failure is translated into a conflict. There is deliberately no
/// "is this slot free?" query before the write: adding one would re-open exactly the race that the
/// filtered unique index removes, and would do so while appearing to improve the error message.
/// </para>
/// <para>
/// The service returns <see cref="OperationResult{TValue}"/> and does not throw for expected
/// outcomes, so that losing a race is ordinary control flow rather than an exception.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Entity access and the commit point.</param>
/// <param name="conflictDetector">Recognises a lost race in a failed save.</param>
/// <param name="clock">Supplies the current date in the schedule's time zone.</param>
/// <param name="options">Supplies how far ahead a slot may be booked.</param>
public sealed class BookingService(
    IUnitOfWork unitOfWork,
    IDatabaseConflictDetector conflictDetector,
    ScheduleClock clock,
    IOptions<BookingOptions> options) : IBookingService
{
    /// <summary>
    /// The single projection from entity to contract.
    /// </summary>
    /// <remarks>
    /// Every <see cref="BookingResponse"/> the system produces — API responses and SignalR
    /// broadcasts alike — is built from this expression, so a client applying a live update and a
    /// client reloading the schedule can never see two different shapes of the same booking.
    /// </remarks>
    private static readonly Expression<Func<Booking, BookingResponse>> ToResponse =
        booking => new BookingResponse(
            booking.Id,
            booking.TimeSlot!.RoomId,
            booking.TimeSlot.Room!.Name,
            booking.TimeSlotId,
            booking.SlotDate,
            booking.TimeSlot.StartTime,
            booking.TimeSlot.EndTime,
            booking.UserId,
            booking.User!.DisplayName,
            booking.CreatedUtc);

    private readonly IUnitOfWork unitOfWork = unitOfWork;
    private readonly IDatabaseConflictDetector conflictDetector = conflictDetector;
    private readonly ScheduleClock clock = clock;
    private readonly BookingOptions options = options.Value;

    /// <inheritdoc />
    public async Task<OperationResult<BookingResponse>> BookAsync(
        Guid timeSlotId,
        DateOnly slotDate,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Today in the schedule's time zone, not the server's. A slot's times are local wall-clock
        // times, so anything else refuses a user their own current day whenever UTC has already
        // rolled over -- or lets them book one that has ended.
        var today = this.clock.Today();

        if (slotDate < today)
        {
            return OperationResult<BookingResponse>.Invalid("A slot in the past cannot be booked.");
        }

        if (slotDate > today.AddDays(this.options.MaxDaysAhead))
        {
            return OperationResult<BookingResponse>.Invalid(
                $"Bookings can be made at most {this.options.MaxDaysAhead} days ahead.");
        }

        // This read establishes that the slot EXISTS and that its room still accepts bookings. It is
        // deliberately NOT an availability check -- it asks nothing about whether the slot is taken,
        // so it does not form the "check" half of a check-then-insert. Availability is decided by
        // the unique index during the INSERT below, and by nothing else.
        var slot = await this.unitOfWork.TimeSlots
            .QueryAsNoTracking()
            .Include(timeSlot => timeSlot.Room)
            .FirstOrDefaultAsync(timeSlot => timeSlot.Id == timeSlotId, cancellationToken);

        if (slot is null)
        {
            return OperationResult<BookingResponse>.NotFound($"Time slot '{timeSlotId}' does not exist.");
        }

        if (slot.Room is null || !slot.Room.IsActive)
        {
            return OperationResult<BookingResponse>.Invalid("This room is no longer available for booking.");
        }

        var booking = new Booking
        {
            TimeSlotId = timeSlotId,
            SlotDate = slotDate,
            UserId = userId,
            Status = BookingStatus.Active,
            CreatedUtc = this.clock.UtcNow(),
        };

        this.unitOfWork.Bookings.Add(booking);

        try
        {
            await this.unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (this.conflictDetector.IsActiveSlotConflict(exception))
        {
            // Another request committed first. This is the expected outcome for every loser of a
            // race, and must reach the caller as 409 rather than 500.
            return OperationResult<BookingResponse>.Conflict(
                "That slot has just been booked by somebody else. Please choose another.");
        }

        var response = await this.ProjectAsync(booking.Id, cancellationToken);

        return response is null
            ? OperationResult<BookingResponse>.NotFound("The booking disappeared immediately after creation.")
            : OperationResult<BookingResponse>.Success(response);
    }

    /// <inheritdoc />
    public async Task<OperationResult<BookingResponse>> CancelAsync(
        Guid bookingId,
        Guid requestingUserId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var booking = await this.unitOfWork.Bookings
            .Query()
            .FirstOrDefaultAsync(candidate => candidate.Id == bookingId, cancellationToken);

        if (booking is null)
        {
            return OperationResult<BookingResponse>.NotFound($"Booking '{bookingId}' does not exist.");
        }

        if (booking.UserId != requestingUserId && !isAdmin)
        {
            return OperationResult<BookingResponse>.Forbidden("You can only cancel your own bookings.");
        }

        if (booking.Status == BookingStatus.Cancelled)
        {
            return OperationResult<BookingResponse>.Conflict("That booking has already been cancelled.");
        }

        // Cancelling sets the status; it never deletes the row. The index filter is what releases
        // the slot, and keeping the row preserves the history of who held it.
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledUtc = this.clock.UtcNow();

        try
        {
            await this.unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The rowversion moved under us: somebody else cancelled or edited this booking first.
            // Complementary to the unique index, which guards creation rather than updates.
            return OperationResult<BookingResponse>.Conflict(
                "That booking was changed by somebody else. Reload and try again.");
        }

        var response = await this.ProjectAsync(booking.Id, cancellationToken);

        return response is null
            ? OperationResult<BookingResponse>.NotFound($"Booking '{bookingId}' does not exist.")
            : OperationResult<BookingResponse>.Success(response);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BookingResponse>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await this.ActiveBookings()
            .Where(booking => booking.UserId == userId)
            .OrderBy(booking => booking.SlotDate)
            .ThenBy(booking => booking.TimeSlot!.StartTime)
            .Select(ToResponse)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BookingResponse>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        await this.ActiveBookings()
            .OrderBy(booking => booking.SlotDate)
            .ThenBy(booking => booking.TimeSlot!.Room!.Name)
            .ThenBy(booking => booking.TimeSlot!.StartTime)
            .Select(ToResponse)
            .ToListAsync(cancellationToken);

    /// <summary>Active bookings, untracked because these paths only project and return.</summary>
    /// <returns>A composable query over active bookings.</returns>
    private IQueryable<Booking> ActiveBookings() =>
        this.unitOfWork.Bookings
            .QueryAsNoTracking()
            .Where(booking => booking.Status == BookingStatus.Active);

    /// <summary>Loads one booking in its contract shape.</summary>
    /// <param name="bookingId">The booking to load.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The projected booking, or <see langword="null"/> if it no longer exists.</returns>
    private Task<BookingResponse?> ProjectAsync(Guid bookingId, CancellationToken cancellationToken) =>
        this.unitOfWork.Bookings
            .QueryAsNoTracking()
            .Where(booking => booking.Id == bookingId)
            .Select(ToResponse)
            .FirstOrDefaultAsync(cancellationToken);
}
