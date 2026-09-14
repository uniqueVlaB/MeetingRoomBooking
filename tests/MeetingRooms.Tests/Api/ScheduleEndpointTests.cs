using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRooms.Contracts.Rooms;
using MeetingRooms.Core.Entities;
using MeetingRooms.Tests.Infrastructure;
using Xunit;

namespace MeetingRooms.Tests.Api;

/// <summary>
/// The schedule: the screen the whole system exists to render, and the one place free and booked
/// slots are distinguished.
/// </summary>
/// <param name="fixture">Shared SQL Server fixture.</param>
[Collection(SqlServerCollection.Name)]
public sealed class ScheduleEndpointTests(SqlServerFixture fixture)
{
    private readonly SqlServerFixture fixture = fixture;

    /// <summary>A schedule is not public.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task FetchingASchedule_WithoutAToken_IsUnauthorized()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(
            $"/api/rooms/{Guid.CreateVersion7()}/schedule?date={TestData.FutureDate():yyyy-MM-dd}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A schedule for a room that does not exist is a 404, not an empty schedule.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task FetchingAScheduleForAnUnknownRoom_IsNotFound()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        ApplicationUser user;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            user = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, user);

        var response = await client.GetAsync(new Uri(
            $"/api/rooms/{Guid.CreateVersion7()}/schedule?date={TestData.FutureDate():yyyy-MM-dd}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>With nothing booked, every slot in the template comes back free and in order.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnEmptyDay_ReturnsEverySlotAsFreeInOrder()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser user;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext, slotCount: 4);
            user = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, user);
        var date = TestData.FutureDate();

        var schedule = await client.GetFromJsonAsync<ScheduleResponse>(
            new Uri($"/api/rooms/{room.Id}/schedule?date={date:yyyy-MM-dd}", UriKind.Relative));

        Assert.NotNull(schedule);
        Assert.Equal(room.Id, schedule.RoomId);
        Assert.Equal(date, schedule.Date);
        Assert.Equal(4, schedule.Slots.Count);
        Assert.Equal([0, 1, 2, 3], schedule.Slots.Select(slot => slot.Ordinal));

        Assert.All(schedule.Slots, slot =>
        {
            Assert.False(slot.IsBooked);
            Assert.Null(slot.BookingId);
            Assert.Null(slot.BookedByUserId);
            Assert.Null(slot.BookedByDisplayName);
        });
    }

    /// <summary>
    /// A booking marks the slot only on the date it was made for.
    /// </summary>
    /// <remarks>
    /// The schedule is queried per room <em>and</em> date. A filter that dropped the date would
    /// show Monday's booking on every day of the year, and the happy-path test would never notice.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ABooking_MarksOnlyTheDateItWasMadeFor()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser user;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext, slotCount: 2);
            user = await TestData.SeedUserAsync(dbContext);
        }

        var slot = room.TimeSlots.OrderBy(candidate => candidate.Ordinal).First();
        var bookedDate = TestData.FutureDate(4);
        var otherDate = TestData.FutureDate(5);

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            await TestData.SeedBookingAsync(dbContext, slot.Id, bookedDate, user.Id);
        }

        using var client = this.CreateClientFor(factory, user);

        var booked = await client.GetFromJsonAsync<ScheduleResponse>(
            new Uri($"/api/rooms/{room.Id}/schedule?date={bookedDate:yyyy-MM-dd}", UriKind.Relative));

        var untouched = await client.GetFromJsonAsync<ScheduleResponse>(
            new Uri($"/api/rooms/{room.Id}/schedule?date={otherDate:yyyy-MM-dd}", UriKind.Relative));

        Assert.NotNull(booked);
        Assert.NotNull(untouched);

        Assert.True(booked.Slots.Single(candidate => candidate.TimeSlotId == slot.Id).IsBooked);
        Assert.All(untouched.Slots, candidate => Assert.False(candidate.IsBooked));
    }

    /// <summary>
    /// A cancelled booking leaves the slot free on the schedule.
    /// </summary>
    /// <remarks>
    /// The schedule must agree with the index filter: cancelled rows are history and hold nothing.
    /// If the two disagreed, a slot could read as taken while the database would happily accept a
    /// booking for it.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ACancelledBooking_LeavesTheSlotFreeOnTheSchedule()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser user;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext);
            user = await TestData.SeedUserAsync(dbContext);
        }

        var slot = room.TimeSlots.Single();
        var date = TestData.FutureDate(6);

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            await TestData.SeedBookingAsync(
                dbContext, slot.Id, date, user.Id, BookingStatus.Cancelled);
        }

        using var client = this.CreateClientFor(factory, user);

        var schedule = await client.GetFromJsonAsync<ScheduleResponse>(
            new Uri($"/api/rooms/{room.Id}/schedule?date={date:yyyy-MM-dd}", UriKind.Relative));

        Assert.NotNull(schedule);

        var entry = schedule.Slots.Single(candidate => candidate.TimeSlotId == slot.Id);
        Assert.False(entry.IsBooked);
        Assert.Null(entry.BookingId);
    }

    /// <summary>A booked slot names its holder, so viewers can see who took it.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ABookedSlot_NamesTheHolder()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser holder;
        ApplicationUser viewer;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext);
            holder = await TestData.SeedUserAsync(dbContext);
            viewer = await TestData.SeedUserAsync(dbContext);
        }

        var slot = room.TimeSlots.Single();
        var date = TestData.FutureDate(7);

        Guid bookingId;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            var booking = await TestData.SeedBookingAsync(dbContext, slot.Id, date, holder.Id);
            bookingId = booking.Id;
        }

        // Viewed by somebody else: the holder's name is shown to whoever is looking at the schedule.
        using var client = this.CreateClientFor(factory, viewer);

        var schedule = await client.GetFromJsonAsync<ScheduleResponse>(
            new Uri($"/api/rooms/{room.Id}/schedule?date={date:yyyy-MM-dd}", UriKind.Relative));

        Assert.NotNull(schedule);

        var entry = schedule.Slots.Single(candidate => candidate.TimeSlotId == slot.Id);
        Assert.True(entry.IsBooked);
        Assert.Equal(bookingId, entry.BookingId);
        Assert.Equal(holder.Id, entry.BookedByUserId);
        Assert.Equal(holder.DisplayName, entry.BookedByDisplayName);
    }

    /// <summary>Creates a client authenticated as a user.</summary>
    /// <param name="factory">The API factory.</param>
    /// <param name="user">The user to authenticate as.</param>
    /// <returns>An authenticated client.</returns>
    private HttpClient CreateClientFor(BookingApiFactory factory, ApplicationUser user)
    {
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken(user, RoleNames.User));

        return client;
    }
}
