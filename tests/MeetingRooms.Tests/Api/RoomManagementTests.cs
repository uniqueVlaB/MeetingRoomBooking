using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRooms.Contracts.Rooms;
using MeetingRooms.Core.Entities;
using MeetingRooms.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MeetingRooms.Tests.Api;

/// <summary>
/// Reading a single room, editing one, and the two very different things deleting one can mean.
/// </summary>
/// <param name="fixture">Shared SQL Server fixture.</param>
[Collection(SqlServerCollection.Name)]
public sealed class RoomManagementTests(SqlServerFixture fixture)
{
    private readonly SqlServerFixture fixture = fixture;

    /// <summary>The catalogue is not public.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ListingRooms_WithoutAToken_IsUnauthorized()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/rooms", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A room can be fetched on its own, with its slots in order.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task FetchingASingleRoom_ReturnsItWithOrderedSlots()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser user;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext, slotCount: 3);
            user = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, user);

        var response = await client.GetFromJsonAsync<RoomResponse>(
            new Uri($"/api/rooms/{room.Id}", UriKind.Relative));

        Assert.NotNull(response);
        Assert.Equal(room.Id, response.Id);
        Assert.Equal(room.Name, response.Name);
        Assert.Equal(3, response.TimeSlots.Count);
        Assert.Equal([0, 1, 2], response.TimeSlots.Select(slot => slot.Ordinal));
    }

    /// <summary>Asking for a room that does not exist is a 404.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task FetchingAnUnknownRoom_IsNotFound()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        ApplicationUser user;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            user = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, user);

        var response = await client.GetAsync(
            new Uri($"/api/rooms/{Guid.CreateVersion7()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>An administrator can rename a room and change its details.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AdministratorCanUpdateARoomsDetails()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser admin;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext, slotCount: 2);
            admin = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, admin, RoleNames.Admin);

        var newName = $"Renamed {Guid.NewGuid():N}";

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/rooms/{room.Id}", UriKind.Relative),
            new UpdateRoomRequest(newName, "New wing", 12, IsActive: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<RoomResponse>();
        Assert.NotNull(updated);
        Assert.Equal(newName, updated.Name);
        Assert.Equal("New wing", updated.Location);
        Assert.Equal(12, updated.Capacity);

        // The slot template is deliberately not editable here, so it must survive untouched.
        Assert.Equal(2, updated.TimeSlots.Count);
    }

    /// <summary>Updating a room that does not exist is a 404.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task UpdatingAnUnknownRoom_IsNotFound()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        ApplicationUser admin;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            admin = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, admin, RoleNames.Admin);

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/rooms/{Guid.CreateVersion7()}", UriKind.Relative),
            new UpdateRoomRequest("Anything", null, 4, IsActive: true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Renaming a room onto another room's name is a conflict, not a server error.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task RenamingARoomOntoATakenName_IsAConflict()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room first;
        Room second;
        ApplicationUser admin;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            first = await TestData.SeedRoomAsync(dbContext);
            second = await TestData.SeedRoomAsync(dbContext);
            admin = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, admin, RoleNames.Admin);

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/rooms/{second.Id}", UriKind.Relative),
            new UpdateRoomRequest(first.Name, null, 4, IsActive: true));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Editing rooms is administrators only.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task UpdatingARoom_IsForbiddenForOrdinaryUsers()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser user;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext);
            user = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, user);

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/rooms/{room.Id}", UriKind.Relative),
            new UpdateRoomRequest("Hijacked", null, 4, IsActive: true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Deleting rooms is administrators only.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task DeletingARoom_IsForbiddenForOrdinaryUsers()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser user;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext);
            user = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, user);

        var response = await client.DeleteAsync(new Uri($"/api/rooms/{room.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A room nobody has ever booked is genuinely removed.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task DeletingARoomThatWasNeverBooked_RemovesIt()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser admin;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext);
            admin = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, admin, RoleNames.Admin);

        var response = await client.DeleteAsync(new Uri($"/api/rooms/{room.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var dbContext2 = this.fixture.CreateDbContext();

        Assert.False(
            await dbContext2.Rooms.AnyAsync(candidate => candidate.Id == room.Id),
            "A room with no booking history should be deleted outright.");
    }

    /// <summary>
    /// A room that has been booked is retired rather than deleted, so the history survives.
    /// </summary>
    /// <remarks>
    /// Deleting would cascade to the room's slots and take every booking of them with it. This is
    /// the branch that keeps past bookings pointing at something real, and it is invisible from the
    /// status code alone — both branches answer 200 — so the database is checked directly.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task DeletingARoomThatHasBookings_RetiresItAndKeepsTheHistory()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Room room;
        ApplicationUser admin;
        Guid bookingId;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            room = await TestData.SeedRoomAsync(dbContext);
            admin = await TestData.SeedUserAsync(dbContext);

            var booking = await TestData.SeedBookingAsync(
                dbContext,
                room.TimeSlots.Single().Id,
                TestData.FutureDate(),
                admin.Id);

            bookingId = booking.Id;
        }

        using var client = this.CreateClientFor(factory, admin, RoleNames.Admin);

        var response = await client.DeleteAsync(new Uri($"/api/rooms/{room.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var dbContext2 = this.fixture.CreateDbContext();

        var stored = await dbContext2.Rooms.SingleOrDefaultAsync(candidate => candidate.Id == room.Id);

        Assert.NotNull(stored);
        Assert.False(stored.IsActive, "A room with bookings should be retired, not removed.");

        Assert.True(
            await dbContext2.Bookings.AnyAsync(booking => booking.Id == bookingId),
            "Retiring a room must not take its booking history with it.");
    }

    /// <summary>Deleting a room that does not exist is a 404.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task DeletingAnUnknownRoom_IsNotFound()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        ApplicationUser admin;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            admin = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, admin, RoleNames.Admin);

        var response = await client.DeleteAsync(
            new Uri($"/api/rooms/{Guid.CreateVersion7()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A room with no slots cannot be created: it would be unbookable.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CreatingARoomWithNoSlots_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        ApplicationUser admin;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            admin = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, admin, RoleNames.Admin);

        var response = await client.PostAsJsonAsync(
            "/api/rooms",
            new CreateRoomRequest($"Room {Guid.NewGuid():N}", null, 4, []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A slot that ends before it starts is rejected.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CreatingARoomWithABackwardsSlot_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        ApplicationUser admin;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            admin = await TestData.SeedUserAsync(dbContext);
        }

        using var client = this.CreateClientFor(factory, admin, RoleNames.Admin);

        var response = await client.PostAsJsonAsync(
            "/api/rooms",
            new CreateRoomRequest(
                $"Room {Guid.NewGuid():N}",
                null,
                4,
                [new TimeSlotRequest(new TimeOnly(11, 0), new TimeOnly(9, 0))]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
