using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRooms.Contracts.Bookings;
using MeetingRooms.Core.Entities;
using MeetingRooms.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MeetingRooms.Tests.Api;

/// <summary>
/// The rules around a booking that are not the concurrency guarantee: which dates are acceptable,
/// what a retired room does, and what the two listings return.
/// </summary>
/// <param name="fixture">Shared SQL Server fixture.</param>
[Collection(SqlServerCollection.Name)]
public sealed class BookingRulesTests(SqlServerFixture fixture)
{
    /// <summary>Mirrors the limit in <c>BookingService</c>.</summary>
    private const int MaxDaysAhead = 365;

    private readonly SqlServerFixture fixture = fixture;

    /// <summary>
    /// Today is bookable.
    /// </summary>
    /// <remarks>
    /// The boundary the "no booking in the past" rule turns on. An off-by-one that compared against
    /// tomorrow would reject the whole of today, which is exactly the day people book most.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task BookingToday_IsAllowed()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (slotId, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        // The server's own answer, not DateOnly.FromDateTime(DateTime.UtcNow): Booking:TimeZone is
        // Europe/Kyiv, which is ahead of UTC, so for a stretch of every day (from local midnight in
        // Kyiv until UTC midnight) UTC's calendar date is still "yesterday" by the server's reckoning
        // and this test would send a date the "no booking in the past" rule correctly rejects.
        var today = factory.Today();

        var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, today));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>The last day inside the booking window is accepted.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task BookingOnTheLastDayOfTheWindow_IsAllowed()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (slotId, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, factory.Today().AddDays(MaxDaysAhead)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// A date past the window is refused.
    /// </summary>
    /// <remarks>
    /// Guards against a mistyped year quietly creating a booking nobody will ever see, rather than
    /// expressing a business rule.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task BookingBeyondTheWindow_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (slotId, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, factory.Today().AddDays(MaxDaysAhead + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A retired room accepts no new bookings, even for slots that still exist.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task BookingASlotInARetiredRoom_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Guid slotId;
        ApplicationUser user;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            var room = await TestData.SeedRoomAsync(dbContext);
            slotId = room.TimeSlots.Single().Id;
            user = await TestData.SeedUserAsync(dbContext);

            room.IsActive = false;
            await dbContext.SaveChangesAsync();
        }

        using var client = this.CreateClientFor(factory, user);

        var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, TestData.FutureDate()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Booking the same slot twice is refused even when it is the same person asking.
    /// </summary>
    /// <remarks>
    /// The index does not care who holds the slot, and neither should the answer. Without this, a
    /// double-click could leave one user holding a slot twice.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task BookingTheSameSlotTwiceAsTheSameUser_IsAConflict()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (slotId, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var slotDate = TestData.FutureDate();
        var request = new CreateBookingRequest(slotId, slotDate);

        var first = await client.PostAsJsonAsync("/api/bookings", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/bookings", request);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>Cancelling a booking that does not exist is a 404, not a 500.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CancellingAnUnknownBooking_IsNotFound()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (_, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var response = await client.DeleteAsync(
            new Uri($"/api/bookings/{Guid.CreateVersion7()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Cancelling twice reports a conflict rather than silently succeeding.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CancellingAnAlreadyCancelledBooking_IsAConflict()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (slotId, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var booked = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, TestData.FutureDate()));

        var booking = await booked.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);

        var uri = new Uri($"/api/bookings/{booking.Id}", UriKind.Relative);

        var first = await client.DeleteAsync(uri);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.DeleteAsync(uri);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>
    /// The personal listing shows the caller's own active bookings, soonest first, and nobody
    /// else's.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task MyBookings_ReturnsOnlyTheCallersActiveBookingsInDateOrder()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser user;
        ApplicationUser stranger;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext, slotCount: 3);
            user = await TestData.SeedUserAsync(dbContext);
            stranger = await TestData.SeedUserAsync(dbContext);
        }

        var slots = room.TimeSlots.OrderBy(slot => slot.Ordinal).ToList();
        var near = TestData.FutureDate(2);
        var far = TestData.FutureDate(9);

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            // Inserted far-first, so passing cannot depend on insertion order.
            await TestData.SeedBookingAsync(dbContext, slots[0].Id, far, user.Id);
            await TestData.SeedBookingAsync(dbContext, slots[1].Id, near, user.Id);

            // Must not appear: cancelled, and somebody else's.
            await TestData.SeedBookingAsync(
                dbContext, slots[2].Id, near, user.Id, BookingStatus.Cancelled);
            await TestData.SeedBookingAsync(dbContext, slots[2].Id, far, stranger.Id);
        }

        using var client = this.CreateClientFor(factory, user);

        var mine = await client.GetFromJsonAsync<IReadOnlyList<BookingResponse>>(
            new Uri("/api/bookings/mine", UriKind.Relative));

        Assert.NotNull(mine);
        Assert.Equal(2, mine.Count);
        Assert.All(mine, booking => Assert.Equal(user.Id, booking.UserId));
        Assert.Equal([near, far], mine.Select(booking => booking.SlotDate));
    }

    /// <summary>The administrators' listing spans every user, while a user's own listing does not.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AllBookings_SpansEveryUserForAnAdministrator()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser owner;
        ApplicationUser other;
        ApplicationUser admin;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext, slotCount: 2);
            owner = await TestData.SeedUserAsync(dbContext);
            other = await TestData.SeedUserAsync(dbContext);
            admin = await TestData.SeedUserAsync(dbContext);
        }

        var slots = room.TimeSlots.OrderBy(slot => slot.Ordinal).ToList();
        var date = TestData.FutureDate(3);

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            await TestData.SeedBookingAsync(dbContext, slots[0].Id, date, owner.Id);
            await TestData.SeedBookingAsync(dbContext, slots[1].Id, date, other.Id);
        }

        using var adminClient = this.CreateClientFor(factory, admin, RoleNames.Admin);

        var all = await adminClient.GetFromJsonAsync<IReadOnlyList<BookingResponse>>(
            new Uri("/api/bookings", UriKind.Relative));

        Assert.NotNull(all);
        Assert.Contains(all, booking => booking.UserId == owner.Id);
        Assert.Contains(all, booking => booking.UserId == other.Id);
    }

    /// <summary>
    /// A cancelled booking leaves the listings.
    /// </summary>
    /// <remarks>
    /// The row survives as history — <c>ActiveSlotIndexTests</c> proves that — but a cancelled
    /// booking is not something the owner still holds, so it must not be listed as one.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ACancelledBooking_DisappearsFromTheListingButSurvivesAsHistory()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        var (slotId, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var booked = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, TestData.FutureDate()));

        var booking = await booked.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);

        await client.DeleteAsync(new Uri($"/api/bookings/{booking.Id}", UriKind.Relative));

        var mine = await client.GetFromJsonAsync<IReadOnlyList<BookingResponse>>(
            new Uri("/api/bookings/mine", UriKind.Relative));

        Assert.NotNull(mine);
        Assert.DoesNotContain(mine, candidate => candidate.Id == booking.Id);

        await using var dbContext = this.fixture.CreateDbContext();

        var stored = await dbContext.Bookings.SingleAsync(candidate => candidate.Id == booking.Id);
        Assert.Equal(BookingStatus.Cancelled, stored.Status);
        Assert.NotNull(stored.CancelledUtc);
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
