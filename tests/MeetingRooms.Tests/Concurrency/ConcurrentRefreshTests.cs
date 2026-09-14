using System.Net;
using System.Net.Http.Json;
using MeetingRooms.Api.Auth;
using MeetingRooms.Contracts.Auth;
using MeetingRooms.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MeetingRooms.Tests.Concurrency;

/// <summary>
/// Redeeming one refresh token twice at the same moment must produce one session, not two.
/// </summary>
/// <remarks>
/// Rotation is a read-then-write — check the token is live, then revoke it and issue a replacement —
/// which is exactly the shape the booking path refuses to have. Two requests arriving together can
/// both pass the check, and without a guard both would go on to mint a replacement: one captured
/// cookie becomes two independent live sessions, and the replay this design claims to detect goes
/// unnoticed. A <c>rowversion</c> on the token is what makes the second writer lose.
/// </remarks>
/// <param name="fixture">Shared SQL Server fixture.</param>
[Collection(SqlServerCollection.Name)]
public sealed class ConcurrentRefreshTests(SqlServerFixture fixture)
{
    /// <summary>How many clients present the same cookie at once.</summary>
    private const int ConcurrentRequests = 8;

    private readonly SqlServerFixture fixture = fixture;

    /// <summary>
    /// Fires several refreshes carrying one cookie simultaneously and asserts that exactly one is
    /// honoured, the rest are refused, and the database records a single replacement token.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SimultaneousRefreshes_RedeemTheTokenOnlyOnce()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        await using (var dbContext = this.fixture.CreateDbContext())
        {
            // Registration puts the account into the User role, and Identity refuses a role that
            // does not exist.
            await TestData.SeedRolesAsync(dbContext);
        }

        var (userId, cookie) = await RegisterAndCaptureCookieAsync(factory);

        // Every request is built first, then waits here. Releasing the gun makes them contend for
        // real instead of trickling in one after another. Asynchronous rather than a Barrier, whose
        // blocking wait would stall the loop that is still starting the other attempts.
        using var startingGun = new SemaphoreSlim(0, ConcurrentRequests);

        var attempts = Enumerable.Range(0, ConcurrentRequests).Select(async _ =>
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
            request.Headers.Add("Cookie", cookie);

            await startingGun.WaitAsync();

            return await client.SendAsync(request);
        }).ToList();

        startingGun.Release(ConcurrentRequests);
        var responses = await Task.WhenAll(attempts);

        try
        {
            var accepted = responses.Count(response => response.StatusCode == HttpStatusCode.OK);
            var refused = responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized);

            Assert.Equal(1, accepted);
            Assert.Equal(ConcurrentRequests - 1, refused);

            // A lost rotation is an expected outcome, not a malfunction; nothing may reach the
            // caller as a 5xx.
            Assert.DoesNotContain(responses, response => (int)response.StatusCode >= 500);

            await using var dbContext = this.fixture.CreateDbContext();

            // Scoped to this account: the fixture's database is shared across the run, and other
            // tests hold sessions of their own.
            //
            // One token was issued at registration and exactly one replacement by the winner. Two
            // replacements would mean two live sessions grown from one cookie.
            var live = await dbContext.RefreshTokens
                .CountAsync(token => token.UserId == userId && token.RevokedUtc == null);

            Assert.Equal(1, live);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>Registers an account and returns it with its refresh cookie, ready to be replayed.</summary>
    /// <param name="factory">The API under test.</param>
    /// <returns>The new account's identifier and the cookie header value to present.</returns>
    private static async Task<(Guid UserId, string Cookie)> RegisterAndCaptureCookieAsync(
        BookingApiFactory factory)
    {
        using var client = factory.CreateClient();

        var registration = new RegisterRequest(
            $"refresh-race-{Guid.NewGuid():N}@meetingrooms.tests",
            "Racer",
            "Test-Password-1");

        using var response = await client.PostAsJsonAsync("/api/auth/register", registration);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.NotNull(session);

        var setCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith($"{RefreshTokenCookie.Name}=", StringComparison.Ordinal));

        // Just the name=value pair; the attributes are the browser's business, not ours.
        return (session.UserId, setCookie.Split(';')[0]);
    }
}
