using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRooms.Api.Hubs;
using MeetingRooms.Api.Realtime;
using MeetingRooms.Contracts.Bookings;
using MeetingRooms.Core.Entities;
using MeetingRooms.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MeetingRooms.Tests.Realtime;

/// <summary>
/// The live-update half of the requirement: everyone watching a schedule is told about a change,
/// and is told about it only when the change actually happened.
/// </summary>
/// <param name="fixture">Shared SQL Server fixture.</param>
[Collection(SqlServerCollection.Name)]
public sealed class BookingBroadcastTests(SqlServerFixture fixture)
{
    private readonly SqlServerFixture fixture = fixture;

    /// <summary>A successful booking is announced, carrying the booking clients need to render.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task BookingASlot_AnnouncesItToScheduleViewers()
    {
        var notifier = new RecordingBookingNotifier();
        await using var factory = this.CreateFactory(notifier);

        var (slotId, roomId, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var slotDate = TestData.FutureDate();

        var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, slotDate));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var broadcast = Assert.Single(notifier.Broadcasts);
        Assert.Equal(nameof(IBookingNotifier.SlotBookedAsync), broadcast.Method);
        Assert.Equal(slotId, broadcast.Booking.TimeSlotId);
        Assert.Equal(roomId, broadcast.Booking.RoomId);
        Assert.Equal(slotDate, broadcast.Booking.SlotDate);
        Assert.Equal(user.Id, broadcast.Booking.UserId);
    }

    /// <summary>Cancelling announces the release, so a waiting viewer sees the slot free up.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CancellingABooking_AnnouncesTheRelease()
    {
        var notifier = new RecordingBookingNotifier();
        await using var factory = this.CreateFactory(notifier);

        var (slotId, _, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var booked = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, TestData.FutureDate()));

        var booking = await booked.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);

        var cancelled = await client.DeleteAsync(
            new Uri($"/api/bookings/{booking.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);

        Assert.Collection(
            notifier.Broadcasts,
            first => Assert.Equal(nameof(IBookingNotifier.SlotBookedAsync), first.Method),
            second =>
            {
                Assert.Equal(nameof(IBookingNotifier.SlotReleasedAsync), second.Method);
                Assert.Equal(booking.Id, second.Booking.Id);
            });
    }

    /// <summary>
    /// Losing the slot announces nothing.
    /// </summary>
    /// <remarks>
    /// The rule is that a broadcast follows a committed write. If the loser of a race also
    /// announced, every viewer would be told the slot changed hands twice and the second message
    /// would describe a booking that was never created.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARejectedBooking_AnnouncesNothing()
    {
        var notifier = new RecordingBookingNotifier();
        await using var factory = this.CreateFactory(notifier);

        var (slotId, _, owner) = await this.SeedSlotAndUserAsync();
        var slotDate = TestData.FutureDate();

        using var ownerClient = this.CreateClientFor(factory, owner);

        var first = await ownerClient.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, slotDate));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Single(notifier.Broadcasts);

        ApplicationUser loser;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            loser = await TestData.SeedUserAsync(dbContext);
        }

        using var loserClient = this.CreateClientFor(factory, loser);

        var second = await loserClient.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, slotDate));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // Still one: the conflict added nothing.
        Assert.Single(notifier.Broadcasts);
    }

    /// <summary>A request that never reaches a write announces nothing either.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ABookingForAnUnknownSlot_AnnouncesNothing()
    {
        var notifier = new RecordingBookingNotifier();
        await using var factory = this.CreateFactory(notifier);

        var (_, _, user) = await this.SeedSlotAndUserAsync();
        using var client = this.CreateClientFor(factory, user);

        var response = await client.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(Guid.CreateVersion7(), TestData.FutureDate()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(notifier.Broadcasts);
    }

    /// <summary>
    /// A failed cancellation announces nothing.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AForbiddenCancellation_AnnouncesNothing()
    {
        var notifier = new RecordingBookingNotifier();
        await using var factory = this.CreateFactory(notifier);

        var (slotId, _, owner) = await this.SeedSlotAndUserAsync();
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

        // Only the original booking was announced; the refused cancellation added nothing.
        Assert.Single(notifier.Broadcasts);
    }

    /// <summary>
    /// The publisher and the subscriber derive the same group name.
    /// </summary>
    /// <remarks>
    /// A mismatch here does not raise an error anywhere: messages are simply delivered to a group
    /// nobody is in, and schedules silently stop updating. Pinning the format is what makes that
    /// failure impossible to introduce quietly.
    /// </remarks>
    [Fact]
    public void TheGroupName_IsScopedToBothRoomAndDate()
    {
        var roomId = Guid.Parse("11112222-3333-4444-5555-666677778888");

        var monday = BookingHub.GroupFor(roomId, new DateOnly(2026, 9, 14));
        var tuesday = BookingHub.GroupFor(roomId, new DateOnly(2026, 9, 15));
        var otherRoom = BookingHub.GroupFor(Guid.CreateVersion7(), new DateOnly(2026, 9, 14));

        Assert.Equal("room:11112222333344445555666677778888:2026-09-14", monday);

        // A browser showing Monday must not be woken by a change to Tuesday, or by another room.
        Assert.NotEqual(monday, tuesday);
        Assert.NotEqual(monday, otherRoom);
    }

    /// <summary>Builds a factory whose notifier records instead of sending.</summary>
    /// <param name="notifier">The recorder to install.</param>
    /// <returns>The factory.</returns>
    private BookingApiFactory CreateFactory(RecordingBookingNotifier notifier) =>
        new(
            this.fixture.ConnectionString,
            services => services.Replace(
                ServiceDescriptor.Singleton<IBookingNotifier>(notifier)));

    /// <summary>Seeds a room with one slot and a user to book it.</summary>
    /// <returns>The slot, its room and the user.</returns>
    private async Task<(Guid SlotId, Guid RoomId, ApplicationUser User)> SeedSlotAndUserAsync()
    {
        await using var dbContext = this.fixture.CreateDbContext();

        var room = await TestData.SeedRoomAsync(dbContext);
        var user = await TestData.SeedUserAsync(dbContext);

        return (room.TimeSlots.Single().Id, room.Id, user);
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
