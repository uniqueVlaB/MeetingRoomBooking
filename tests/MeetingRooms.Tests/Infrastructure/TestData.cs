using MeetingRooms.Core.Entities;
using MeetingRooms.Infrastructure.SQL.Database;

namespace MeetingRooms.Tests.Infrastructure;

/// <summary>Builds the fixtures a booking test needs: users, rooms and slots.</summary>
public static class TestData
{
    /// <summary>
    /// Inserts a user directly, bypassing <c>UserManager</c>.
    /// </summary>
    /// <remarks>
    /// The tests authenticate with tokens minted by the real token service, so no password hash is
    /// ever checked. Inserting the row directly keeps setup fast, which matters when a single test
    /// needs twenty users.
    /// </remarks>
    /// <param name="dbContext">Context to write through.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created user.</returns>
    public static async Task<ApplicationUser> SeedUserAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var user = NewUser();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return user;
    }

    /// <summary>Inserts several users in one round-trip.</summary>
    /// <param name="dbContext">Context to write through.</param>
    /// <param name="count">How many users to create.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created users.</returns>
    public static async Task<IReadOnlyList<ApplicationUser>> SeedUsersAsync(
        AppDbContext dbContext,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var users = Enumerable.Range(0, count).Select(_ => NewUser()).ToList();

        dbContext.Users.AddRange(users);
        await dbContext.SaveChangesAsync(cancellationToken);

        return users;
    }

    /// <summary>Inserts a room with an hourly slot template starting at 09:00.</summary>
    /// <param name="dbContext">Context to write through.</param>
    /// <param name="slotCount">How many one-hour slots the room has.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created room, with its slots populated.</returns>
    public static async Task<Room> SeedRoomAsync(
        AppDbContext dbContext,
        int slotCount = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var room = new Room { Name = $"Room {suffix}", Location = "Test wing", Capacity = 6 };

        for (var index = 0; index < slotCount; index++)
        {
            room.TimeSlots.Add(new TimeSlot
            {
                RoomId = room.Id,
                StartTime = new TimeOnly(9 + index, 0),
                EndTime = new TimeOnly(10 + index, 0),
                Ordinal = index,
            });
        }

        dbContext.Rooms.Add(room);
        await dbContext.SaveChangesAsync(cancellationToken);

        return room;
    }

    /// <summary>A date safely in the future, so the "no booking in the past" rule never interferes.</summary>
    /// <param name="daysAhead">How far ahead.</param>
    /// <returns>The date.</returns>
    public static DateOnly FutureDate(int daysAhead = 1) =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysAhead));

    /// <summary>Builds an unsaved user with unique, valid Identity fields.</summary>
    /// <returns>The user.</returns>
    private static ApplicationUser NewUser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];

        return new ApplicationUser
        {
            UserName = $"user-{suffix}@meetingrooms.test",
            NormalizedUserName = $"USER-{suffix}@MEETINGROOMS.TEST",
            Email = $"user-{suffix}@meetingrooms.test",
            NormalizedEmail = $"USER-{suffix}@MEETINGROOMS.TEST",
            EmailConfirmed = true,
            DisplayName = $"Test User {suffix[..4]}",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
    }
}
