using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRooms.Api.Realtime;
using MeetingRooms.Contracts.Bookings;
using MeetingRooms.Core.Entities;
using MeetingRooms.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace MeetingRooms.Tests.Concurrency;

/// <summary>
/// The other side of the race: several cancellations of one booking arriving together.
/// </summary>
/// <remarks>
/// Creation is guarded by the unique index. Cancellation is an update, which the index says nothing
/// about, so it is guarded by the row version instead — and that path has its own way of failing.
/// A double-clicked cancel button, or a user cancelling from two tabs, must produce one release and
/// a clean refusal, never a 500 and never two releases announced to everyone watching.
/// </remarks>
/// <param name="fixture">Shared SQL Server fixture.</param>
/// <param name="output">xUnit output, used to report the observed status-code spread.</param>
[Collection(SqlServerCollection.Name)]
public sealed class ConcurrentCancellationTests(SqlServerFixture fixture, ITestOutputHelper output)
{
    /// <summary>How many cancellations race for the same booking.</summary>
    private const int ConcurrentRequests = 10;

    private readonly SqlServerFixture fixture = fixture;
    private readonly ITestOutputHelper output = output;

    /// <summary>
    /// Ten simultaneous cancellations of one booking release it exactly once.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SimultaneousCancellations_ReleaseTheBookingExactlyOnce()
    {
        var notifier = new RecordingBookingNotifier();

        await using var factory = new BookingApiFactory(
            this.fixture.ConnectionString,
            services => services.Replace(ServiceDescriptor.Singleton<IBookingNotifier>(notifier)));

        Guid slotId;
        ApplicationUser owner;

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            var room = await TestData.SeedRoomAsync(dbContext);
            slotId = room.TimeSlots.Single().Id;
            owner = await TestData.SeedUserAsync(dbContext);
        }

        using var setupClient = this.CreateClientFor(factory, owner);

        var booked = await setupClient.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequest(slotId, TestData.FutureDate()));

        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);

        var booking = await booked.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);

        // Built and authenticated first, then released together, so they genuinely contend.
        using var startingGun = new SemaphoreSlim(0, ConcurrentRequests);

        var attempts = Enumerable.Range(0, ConcurrentRequests).Select(async _ =>
        {
            using var client = this.CreateClientFor(factory, owner);

            await startingGun.WaitAsync();

            return await client.DeleteAsync(new Uri($"/api/bookings/{booking.Id}", UriKind.Relative));
        }).ToList();

        startingGun.Release(ConcurrentRequests);
        var responses = await Task.WhenAll(attempts);

        try
        {
            var released = responses.Count(response => response.StatusCode == HttpStatusCode.NoContent);
            var conflicts = responses.Count(response => response.StatusCode == HttpStatusCode.Conflict);
            var serverErrors = responses.Count(response => (int)response.StatusCode >= 500);

            this.output.WriteLine(
                "released={0} conflict={1} serverError={2} other={3}",
                released,
                conflicts,
                serverErrors,
                responses.Length - released - conflicts - serverErrors);

            // Exactly one cancellation takes effect.
            Assert.Equal(1, released);

            // The rest are told plainly that somebody got there first.
            Assert.Equal(ConcurrentRequests - 1, conflicts);

            // A lost update escaping as an exception is the failure this guards against.
            Assert.Equal(0, serverErrors);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var verifyContext = this.fixture.CreateDbContext();

        var stored = await verifyContext.Bookings.SingleAsync(candidate => candidate.Id == booking.Id);
        Assert.Equal(BookingStatus.Cancelled, stored.Status);

        // Viewers are told the slot freed up once, not ten times.
        Assert.Single(
            notifier.Broadcasts,
            broadcast => broadcast.Method == nameof(IBookingNotifier.SlotReleasedAsync));
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
