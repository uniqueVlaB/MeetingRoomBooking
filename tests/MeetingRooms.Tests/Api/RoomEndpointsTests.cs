using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRooms.Contracts.Rooms;
using MeetingRooms.Core.Entities;
using MeetingRooms.Tests.Infrastructure;
using Xunit;

namespace MeetingRooms.Tests.Api;

/// <summary>
/// The room endpoints' contract, and in particular the boundary between what a user may do and what
/// only an administrator may do.
/// </summary>
/// <param name="fixture">Shared SQL Server fixture.</param>
[Collection(SqlServerCollection.Name)]
public sealed class RoomEndpointsTests(SqlServerFixture fixture)
{
    private readonly SqlServerFixture fixture = fixture;

    /// <summary>Creating a room is administrators only.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CreatingARoom_IsForbiddenForOrdinaryUsers()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        using var client = await this.CreateClientAsync(factory, RoleNames.User);

        var response = await client.PostAsJsonAsync("/api/rooms", ValidRoom($"Room {Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator can create a room, and it comes back with ordered slots.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AdministratorCanCreateARoom()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        using var client = await this.CreateClientAsync(factory, RoleNames.Admin);

        var response = await client.PostAsJsonAsync("/api/rooms", ValidRoom($"Room {Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var room = await response.Content.ReadFromJsonAsync<RoomResponse>();
        Assert.NotNull(room);
        Assert.Equal(2, room.TimeSlots.Count);

        // Ordinal comes from chronological order, not from the order they were submitted in.
        Assert.Equal([0, 1], room.TimeSlots.Select(slot => slot.Ordinal));
        Assert.Equal(new TimeOnly(9, 0), room.TimeSlots[0].StartTime);
    }

    /// <summary>A duplicate room name is a conflict, not a server error.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CreatingARoomWithATakenName_IsAConflict()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        using var client = await this.CreateClientAsync(factory, RoleNames.Admin);

        var name = $"Room {Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/rooms", ValidRoom(name));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/rooms", ValidRoom(name));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>Overlapping slots are rejected: they would let the same hour be booked twice.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task CreatingARoomWithOverlappingSlots_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        using var client = await this.CreateClientAsync(factory, RoleNames.Admin);

        var request = new CreateRoomRequest(
            $"Room {Guid.NewGuid():N}",
            "Test wing",
            6,
            [
                new TimeSlotRequest(new TimeOnly(9, 0), new TimeOnly(11, 0)),
                new TimeSlotRequest(new TimeOnly(10, 0), new TimeOnly(12, 0)),
            ]);

        var response = await client.PostAsJsonAsync("/api/rooms", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Ordinary users can read the catalogue.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task OrdinaryUsersCanListRooms()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            await TestData.SeedRoomAsync(dbContext, slotCount: 2);
        }

        using var client = await this.CreateClientAsync(factory, RoleNames.User);

        var rooms = await client.GetFromJsonAsync<IReadOnlyList<RoomResponse>>(
            new Uri("/api/rooms", UriKind.Relative));

        Assert.NotNull(rooms);
        Assert.NotEmpty(rooms);
    }

    /// <summary>A retired room disappears for users but stays visible to administrators.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task RetiredRooms_AreHiddenFromUsersButVisibleToAdministrators()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        using var adminClient = await this.CreateClientAsync(factory, RoleNames.Admin);

        var created = await adminClient.PostAsJsonAsync(
            "/api/rooms",
            ValidRoom($"Room {Guid.NewGuid():N}"));

        var room = await created.Content.ReadFromJsonAsync<RoomResponse>();
        Assert.NotNull(room);

        var updated = await adminClient.PutAsJsonAsync(
            new Uri($"/api/rooms/{room.Id}", UriKind.Relative),
            new UpdateRoomRequest(room.Name, room.Location, room.Capacity, IsActive: false));

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var userClient = await this.CreateClientAsync(factory, RoleNames.User);

        var visibleToUser = await userClient.GetFromJsonAsync<IReadOnlyList<RoomResponse>>(
            new Uri("/api/rooms", UriKind.Relative));

        var visibleToAdmin = await adminClient.GetFromJsonAsync<IReadOnlyList<RoomResponse>>(
            new Uri("/api/rooms", UriKind.Relative));

        Assert.NotNull(visibleToUser);
        Assert.NotNull(visibleToAdmin);
        Assert.DoesNotContain(visibleToUser, candidate => candidate.Id == room.Id);
        Assert.Contains(visibleToAdmin, candidate => candidate.Id == room.Id);
    }

    /// <summary>Builds a valid two-slot room request.</summary>
    /// <param name="name">Room name.</param>
    /// <returns>The request.</returns>
    private static CreateRoomRequest ValidRoom(string name) => new(
        name,
        "Test wing",
        6,
        [
            // Deliberately out of order, so the ordinal assignment is exercised.
            new TimeSlotRequest(new TimeOnly(10, 0), new TimeOnly(11, 0)),
            new TimeSlotRequest(new TimeOnly(9, 0), new TimeOnly(10, 0)),
        ]);

    /// <summary>Creates a client authenticated in a role.</summary>
    /// <param name="factory">The API factory.</param>
    /// <param name="role">The role to hold.</param>
    /// <returns>An authenticated client.</returns>
    private async Task<HttpClient> CreateClientAsync(BookingApiFactory factory, string role)
    {
        ApplicationUser user;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            user = await TestData.SeedUserAsync(dbContext);
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken(user, role));

        return client;
    }
}
