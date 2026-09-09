using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRooms.Contracts.Bookings;
using MeetingRooms.Core.Entities;
using MeetingRooms.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace MeetingRooms.Tests.Concurrency;

/// <summary>
/// The test the task asks for: many simultaneous requests for one slot, exactly one booking created.
/// </summary>
/// <remarks>
/// Requests are held at a barrier and released together so that they genuinely contend. Firing them
/// in a loop would not test anything interesting: the second request would simply find the slot
/// already taken, which is the easy case. The hard case is several requests reaching the INSERT
/// inside the same instant, and that is what the barrier arranges.
/// </remarks>
/// <param name="fixture">Shared SQL Server fixture.</param>
/// <param name="output">xUnit output, used to report the observed status-code spread.</param>
[Collection(SqlServerCollection.Name)]
public sealed class ConcurrentBookingTests(SqlServerFixture fixture, ITestOutputHelper output)
{
    /// <summary>How many clients race for the slot.</summary>
    private const int ConcurrentRequests = 20;

    private readonly SqlServerFixture fixture = fixture;
    private readonly ITestOutputHelper output = output;

    /// <summary>
    /// Fires <see cref="ConcurrentRequests"/> booking requests at one slot simultaneously and
    /// asserts that exactly one wins, every other receives 409, nobody receives a 5xx, and the
    /// database holds exactly one active booking.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TwentySimultaneousRequests_CreateExactlyOneBooking()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        Guid slotId;
        IReadOnlyList<ApplicationUser> users;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            var room = await TestData.SeedRoomAsync(dbContext);
            slotId = room.TimeSlots.Single().Id;

            // Distinct users, so nothing but the slot itself can serialise the requests. Twenty
            // requests from one account might contend on that account's rows instead and pass for
            // the wrong reason.
            users = await TestData.SeedUsersAsync(dbContext, ConcurrentRequests);
        }

        var slotDate = TestData.FutureDate();
        var request = new CreateBookingRequest(slotId, slotDate);

        // Every request is built and authenticated first, then waits here. Releasing the barrier
        // makes them contend for real instead of trickling in one after another.
        using var startingGun = new SemaphoreSlim(0, ConcurrentRequests);

        var attempts = users.Select(async user =>
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                factory.CreateAccessToken(user, RoleNames.User));

            await startingGun.WaitAsync();

            return await client.PostAsJsonAsync("/api/bookings", request);
        }).ToList();

        startingGun.Release(ConcurrentRequests);
        var responses = await Task.WhenAll(attempts);

        try
        {
            var created = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
            var conflicts = responses.Count(response => response.StatusCode == HttpStatusCode.Conflict);
            var serverErrors = responses.Count(response => (int)response.StatusCode >= 500);

            this.output.WriteLine(
                "created={0} conflict={1} serverError={2} other={3}",
                created,
                conflicts,
                serverErrors,
                responses.Length - created - conflicts - serverErrors);

            // Exactly one winner.
            Assert.Equal(1, created);

            // Everyone else gets a clear conflict -- not a silent failure, and not a crash.
            Assert.Equal(ConcurrentRequests - 1, conflicts);

            // A 5xx here would mean a lost race escaped as an exception rather than being
            // translated, which the task explicitly rules out.
            Assert.Equal(0, serverErrors);

            // And the database agrees with the status codes: one active booking, not twenty rows
            // that the API merely reported as conflicts.
            await using var verifyContext = this.fixture.CreateDbContext();

            var activeBookings = await verifyContext.Bookings.CountAsync(booking =>
                booking.TimeSlotId == slotId
                && booking.SlotDate == slotDate
                && booking.Status == BookingStatus.Active);

            Assert.Equal(1, activeBookings);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>
    /// The same race, run repeatedly. A single green run is weak evidence about a race condition;
    /// this catches an interleaving that happens to be rare.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task RepeatedRaces_NeverProduceASecondBooking()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        const int Rounds = 5;
        const int RequestsPerRound = 8;

        for (var round = 0; round < Rounds; round++)
        {
            Guid slotId;
            IReadOnlyList<ApplicationUser> users;

            await using (var dbContext = this.fixture.CreateDbContext())
            {
                var room = await TestData.SeedRoomAsync(dbContext);
                slotId = room.TimeSlots.Single().Id;
                users = await TestData.SeedUsersAsync(dbContext, RequestsPerRound);
            }

            var slotDate = TestData.FutureDate(round + 1);
            var request = new CreateBookingRequest(slotId, slotDate);

            using var startingGun = new SemaphoreSlim(0, RequestsPerRound);

            var attempts = users.Select(async user =>
            {
                using var client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    factory.CreateAccessToken(user, RoleNames.User));

                await startingGun.WaitAsync();

                return await client.PostAsJsonAsync("/api/bookings", request);
            }).ToList();

            startingGun.Release(RequestsPerRound);
            var responses = await Task.WhenAll(attempts);

            var created = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
            var serverErrors = responses.Count(response => (int)response.StatusCode >= 500);

            foreach (var response in responses)
            {
                response.Dispose();
            }

            Assert.Equal(1, created);
            Assert.Equal(0, serverErrors);
        }
    }
}
