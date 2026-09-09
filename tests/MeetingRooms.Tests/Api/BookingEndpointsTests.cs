using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRooms.Contracts.Bookings;
using MeetingRooms.Contracts.Rooms;
using MeetingRooms.Core.Entities;
using MeetingRooms.Tests.Infrastructure;
using Xunit;

namespace MeetingRooms.Tests.Api;

/// <summary>
/// The booking endpoints' contract: who may do what, and what each outcome looks like to a client.
/// </summary>
/// <param name="fixture">Shared SQL Server fixture.</param>
[Collection(SqlServerCollection.Name)]
public sealed class BookingEndpointsTests(SqlServerFixture fixture)
{
    private readonly SqlServerFixture fixture = fixture;

    /// <summary>Booking requires a signed-in caller.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Booking_WithoutAToken_IsUnauthorized()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(Guid.CreateVersion7(), TestData.FutureDate()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A slot in the past is rejected as a bad request, not silently accepted.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task BookingAPastDate_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (slotId, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, TestData.FutureDate(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Booking a slot that does not exist is a 404, not a 500.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task BookingAnUnknownSlot_IsNotFound()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (_, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(Guid.CreateVersion7(), TestData.FutureDate()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Cancelling frees the slot, and the same slot can then be booked by somebody else — the
    /// end-to-end version of what <c>ActiveSlotIndexTests</c> proves at the schema level.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CancellingABooking_LetsAnotherUserBookTheSlot()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (slotId, owner) = await this.SeedSlotAndUserAsync();
        var slotDate = TestData.FutureDate();

        using var ownerClient = this.CreateClientFor(factory, owner);

        var booked = await ownerClient.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, slotDate));

        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        var booking = await booked.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);

        // A second user cannot take the slot while it is held.
        ApplicationUser other;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            other = await TestData.SeedUserAsync(dbContext);
        }

        using var otherClient = this.CreateClientFor(factory, other);

        var blocked = await otherClient.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, slotDate));

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // Cancel, and the slot becomes available again.
        var cancelled = await ownerClient.DeleteAsync(new Uri($"/api/bookings/{booking.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);

        var rebooked = await otherClient.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, slotDate));

        Assert.Equal(HttpStatusCode.Created, rebooked.StatusCode);
    }

    /// <summary>A user cannot cancel somebody else's booking.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CancellingSomebodyElsesBooking_IsForbidden()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (slotId, owner) = await this.SeedSlotAndUserAsync();
        using var ownerClient = this.CreateClientFor(factory, owner);

        var booked = await ownerClient.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, TestData.FutureDate()));

        var booking = await booked.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);

        ApplicationUser stranger;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            stranger = await TestData.SeedUserAsync(dbContext);
        }

        using var strangerClient = this.CreateClientFor(factory, stranger);

        var response = await strangerClient.DeleteAsync(
            new Uri($"/api/bookings/{booking.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator can cancel a booking they do not own.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AdministratorCanCancelAnybodysBooking()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (slotId, owner) = await this.SeedSlotAndUserAsync();
        using var ownerClient = this.CreateClientFor(factory, owner);

        var booked = await ownerClient.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, TestData.FutureDate()));

        var booking = await booked.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);

        ApplicationUser admin;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            admin = await TestData.SeedUserAsync(dbContext);
        }

        using var adminClient = this.CreateClientFor(factory, admin, RoleNames.Admin);

        var response = await adminClient.DeleteAsync(
            new Uri($"/api/bookings/{booking.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>The cross-user listing is administrators only.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ListingAllBookings_IsForbiddenForOrdinaryUsers()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (_, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var response = await client.GetAsync(new Uri("/api/bookings", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A booking shows up on the room's schedule for that date.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ABookedSlot_AppearsAsBookedOnTheSchedule()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser user;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext, slotCount: 3);
            user = await TestData.SeedUserAsync(dbContext);
        }

        var slot = room.TimeSlots.OrderBy(candidate => candidate.Ordinal).First();
        var slotDate = TestData.FutureDate();

        using var client = this.CreateClientFor(factory, user);

        await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(slot.Id, slotDate));

        var schedule = await client.GetFromJsonAsync<ScheduleResponse>(
            new Uri($"/api/rooms/{room.Id}/schedule?date={slotDate:yyyy-MM-dd}", UriKind.Relative));

        Assert.NotNull(schedule);
        Assert.Equal(3, schedule.Slots.Count);

        var booked = schedule.Slots.Single(candidate => candidate.TimeSlotId == slot.Id);
        Assert.True(booked.IsBooked);
        Assert.Equal(user.Id, booked.BookedByUserId);
        Assert.Equal(user.DisplayName, booked.BookedByDisplayName);

        // The other slots are untouched.
        Assert.All(
            schedule.Slots.Where(candidate => candidate.TimeSlotId != slot.Id),
            candidate => Assert.False(candidate.IsBooked));
    }

    /// <summary>Seeds a room with one slot and a user to book it.</summary>
    /// <returns>The slot identifier and the user.</returns>
    private async Task<(Guid SlotId, ApplicationUser User)> SeedSlotAndUserAsync()
    {
        await using var dbContext = this.fixture.CreateDbContext();

        var room = await TestData.SeedRoomAsync(dbContext);
        var user = await TestData.SeedUserAsync(dbContext);

        return (room.TimeSlots.Single().Id, user);
    }

    /// <summary>Creates a client authenticated as a user.</summary>
    /// <param name="factory">The API factory.</param>
    /// <param name="user">The user to authenticate as.</param>
    /// <param name="roles">Roles to embed; defaults to the ordinary user role.</param>
    /// <returns>An authenticated client.</returns>
    private HttpClient CreateClientFor(
        BookingApiFactory factory,
        ApplicationUser user,
        params string[] roles)
    {
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken(user, roles.Length == 0 ? [RoleNames.User] : roles));

        return client;
    }
}
