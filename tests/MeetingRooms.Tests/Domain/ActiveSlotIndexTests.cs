using MeetingRooms.Core.Entities;
using MeetingRooms.Infrastructure.SQL.Concurrency;
using MeetingRooms.Infrastructure.SQL.Database;
using MeetingRooms.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MeetingRooms.Tests.Domain;

/// <summary>
/// Proves the database-level guarantee the whole design rests on: at most one active booking per
/// slot and date, enforced by <c>UX_Bookings_ActiveSlot</c>.
/// </summary>
/// <remarks>
/// These bypass the application entirely and write through the context, which is the point. The
/// application cannot double-book because the schema does not permit it — not because the service
/// remembers to check.
/// </remarks>
/// <param name="fixture">Shared SQL Server fixture.</param>
[Collection(SqlServerCollection.Name)]
public sealed class ActiveSlotIndexTests(SqlServerFixture fixture)
{
    private readonly SqlServerFixture fixture = fixture;

    /// <summary>A second active booking for the same slot and date is rejected by the database.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SecondActiveBookingForSameSlotAndDate_IsRejected()
    {
        await using var dbContext = this.fixture.CreateDbContext();
        var user = await TestData.SeedUserAsync(dbContext);
        var room = await TestData.SeedRoomAsync(dbContext);
        var slotId = room.TimeSlots.Single().Id;
        var date = new DateOnly(2026, 10, 1);

        dbContext.Bookings.Add(NewBooking(slotId, date, user.Id));
        await dbContext.SaveChangesAsync();

        dbContext.Bookings.Add(NewBooking(slotId, date, user.Id));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        // And the failure is recognisable as *this* conflict, which is what lets the service turn it
        // into a 409 rather than letting it escape as a 500.
        Assert.True(
            new SqlServerConflictDetector().IsActiveSlotConflict(exception),
            "The unique violation should be attributed to the active-slot index.");
    }

    /// <summary>The same slot on a different date is unaffected.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task BookingSameSlotOnDifferentDate_IsAllowed()
    {
        await using var dbContext = this.fixture.CreateDbContext();
        var user = await TestData.SeedUserAsync(dbContext);
        var room = await TestData.SeedRoomAsync(dbContext);
        var slotId = room.TimeSlots.Single().Id;

        dbContext.Bookings.Add(NewBooking(slotId, new DateOnly(2026, 10, 1), user.Id));
        dbContext.Bookings.Add(NewBooking(slotId, new DateOnly(2026, 10, 2), user.Id));

        await dbContext.SaveChangesAsync();

        Assert.Equal(2, await dbContext.Bookings.CountAsync(booking => booking.TimeSlotId == slotId));
    }

    /// <summary>
    /// Cancelling releases the slot: the index filter excludes cancelled rows, so the slot can be
    /// booked again while the cancelled booking is kept for history.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CancellingABooking_FreesTheSlotForReuse()
    {
        await using var dbContext = this.fixture.CreateDbContext();
        var user = await TestData.SeedUserAsync(dbContext);
        var room = await TestData.SeedRoomAsync(dbContext);
        var slotId = room.TimeSlots.Single().Id;
        var date = new DateOnly(2026, 10, 3);

        var first = NewBooking(slotId, date, user.Id);
        dbContext.Bookings.Add(first);
        await dbContext.SaveChangesAsync();

        first.Status = BookingStatus.Cancelled;
        first.CancelledUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        dbContext.Bookings.Add(NewBooking(slotId, date, user.Id));
        await dbContext.SaveChangesAsync();

        // Two rows survive -- the cancelled one is history -- but only one of them holds the slot.
        Assert.Equal(
            2,
            await dbContext.Bookings.CountAsync(booking =>
                booking.TimeSlotId == slotId && booking.SlotDate == date));

        Assert.Equal(
            1,
            await dbContext.Bookings.CountAsync(booking =>
                booking.TimeSlotId == slotId
                && booking.SlotDate == date
                && booking.Status == BookingStatus.Active));
    }

    /// <summary>
    /// A duplicate room name is a unique violation, but it is not a slot conflict.
    /// </summary>
    /// <remarks>
    /// Guards the detail that makes the detector trustworthy: it matches on the index name, so an
    /// unrelated duplicate is never reported to a user as "this slot is already booked".
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task DuplicateRoomName_IsNotMistakenForASlotConflict()
    {
        await using var dbContext = this.fixture.CreateDbContext();
        var room = await TestData.SeedRoomAsync(dbContext);

        dbContext.Rooms.Add(new Room { Name = room.Name, Capacity = 2 });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        var detector = new SqlServerConflictDetector();

        Assert.True(detector.IsUniqueViolation(exception));
        Assert.False(detector.IsActiveSlotConflict(exception));
    }

    /// <summary>Builds an active booking.</summary>
    /// <param name="slotId">Slot to book.</param>
    /// <param name="date">Date to book it for.</param>
    /// <param name="userId">Owner.</param>
    /// <returns>The unsaved booking.</returns>
    private static Booking NewBooking(Guid slotId, DateOnly date, Guid userId) => new()
    {
        TimeSlotId = slotId,
        SlotDate = date,
        UserId = userId,
        Status = BookingStatus.Active,
    };
}
