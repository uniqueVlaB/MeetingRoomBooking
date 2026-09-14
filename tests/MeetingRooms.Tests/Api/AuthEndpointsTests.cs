using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using MeetingRooms.Api.Auth;
using MeetingRooms.Contracts.Auth;
using MeetingRooms.Core.Entities;
using MeetingRooms.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace MeetingRooms.Tests.Api;

/// <summary>
/// The session lifecycle: registering, signing in, refreshing and signing out.
/// </summary>
/// <remarks>
/// The security properties here are the ones that do not fail loudly when they break. A refresh
/// token that is readable from JavaScript, or one that still works after it has been redeemed,
/// leaves every functional test green — so each is asserted directly rather than inferred from the
/// happy path working.
/// </remarks>
/// <param name="fixture">Shared SQL Server fixture.</param>
[Collection(SqlServerCollection.Name)]
public sealed class AuthEndpointsTests(SqlServerFixture fixture)
{
    private readonly SqlServerFixture fixture = fixture;

    /// <summary>Registering creates an ordinary user and returns a usable session.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Registering_CreatesAnOrdinaryUserAndStartsASession()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();
        var request = NewRegistration();

        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(session);
        Assert.Equal(request.Email, session.Email);
        Assert.Equal(request.DisplayName, session.DisplayName);
        Assert.NotEqual(Guid.Empty, session.UserId);
        Assert.NotEmpty(session.AccessToken);

        // Registration never grants administrator: that would make the role meaningless.
        Assert.Equal([RoleNames.User], session.Roles);
    }

    /// <summary>The access token registration hands out actually opens protected endpoints.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheAccessTokenFromRegistration_IsAcceptedByAProtectedEndpoint()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();

        var session = await RegisterAsync(client);

        using var authenticated = factory.CreateClient();
        authenticated.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var response = await authenticated.GetAsync(new Uri("/api/bookings/mine", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>An email address can only be registered once.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task RegisteringAnEmailTwice_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();
        var request = NewRegistration();

        var first = await client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    /// <summary>Passwords below the configured length are refused.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task RegisteringWithAShortPassword_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            NewRegistration() with { Password = "Sh0rt" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A malformed email never reaches Identity: the contract's annotations stop it.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task RegisteringWithAMalformedEmail_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            NewRegistration() with { Email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Correct credentials produce a session.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SigningIn_WithCorrectCredentials_ReturnsASession()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var registrationClient = factory.CreateClient();
        var registration = NewRegistration();
        await registrationClient.PostAsJsonAsync("/api/auth/register", registration);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(registration.Email, registration.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(session);
        Assert.Equal(registration.Email, session.Email);
        Assert.NotEmpty(session.AccessToken);
    }

    /// <summary>
    /// An unknown account and a wrong password are answered identically.
    /// </summary>
    /// <remarks>
    /// If the two differed in status or in wording, the endpoint would be an oracle for discovering
    /// which email addresses hold accounts. This asserts the responses are indistinguishable.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SigningIn_DoesNotRevealWhetherAnAccountExists()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var registrationClient = factory.CreateClient();
        var registration = NewRegistration();
        await registrationClient.PostAsJsonAsync("/api/auth/register", registration);

        using var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(registration.Email, "WrongPassword123"));

        var unknownAccount = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest($"absent-{Guid.NewGuid():N}@meetingrooms.test", "WrongPassword123"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);

        // Compared field by field rather than as whole bodies: problem details carry a per-request
        // traceId, which differs every time and tells a caller nothing about the account.
        Assert.Equal(
            await ReadProblemAsync(wrongPassword),
            await ReadProblemAsync(unknownAccount));
    }

    /// <summary>The refresh cookie cannot be read by scripts and is not sent to every endpoint.</summary>
    /// <remarks>
    /// The whole reason the refresh token lives in a cookie rather than in <c>localStorage</c> is
    /// <c>HttpOnly</c>. Losing that attribute would silently undo the defence, so it is asserted
    /// rather than assumed.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheRefreshCookie_IsHttpOnlyAndScopedToTheAuthEndpoints()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var cookie = ReadSetCookie(response);

        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The database stores a hash, never the token itself.
    /// </summary>
    /// <remarks>
    /// A leaked backup should not hand over live sessions. This checks the raw cookie value appears
    /// in no token row.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheRawRefreshToken_IsNeverStored()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var rawToken = ReadCookieValue(response);
        Assert.NotNull(rawToken);

        await using var dbContext = this.fixture.CreateDbContext();

        Assert.False(
            await dbContext.Set<RefreshToken>().AnyAsync(token => token.TokenHash == rawToken),
            "The refresh token was stored verbatim; only its hash should ever reach the database.");
    }

    /// <summary>Refreshing returns a new access token and replaces the cookie.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Refreshing_ReturnsANewAccessTokenAndRotatesTheCookie()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();

        var registration = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var originalToken = ReadCookieValue(registration);

        var response = await client.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(session);
        Assert.NotEmpty(session.AccessToken);

        // Rotation means a different token comes back, not the same one echoed.
        var rotatedToken = ReadCookieValue(response);
        Assert.NotNull(rotatedToken);
        Assert.NotEqual(originalToken, rotatedToken);
    }

    /// <summary>
    /// A refresh token stops working the moment it is redeemed.
    /// </summary>
    /// <remarks>
    /// This is what makes rotation worth doing: a cookie captured earlier is useless once the real
    /// owner has refreshed with it. Without this assertion a service that issued a replacement but
    /// forgot to revoke the original would look perfectly healthy.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AReplayedRefreshToken_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();

        var registration = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var capturedToken = ReadCookieValue(registration);
        Assert.NotNull(capturedToken);

        // The legitimate owner refreshes, which revokes the captured value.
        var rotated = await client.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        // An attacker replays the earlier cookie from a different client.
        using var replayClient = factory.CreateClient();

        var response = await replayClient.SendAsync(BuildRefreshRequest(capturedToken));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Refreshing without a cookie is refused rather than throwing.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task RefreshingWithoutACookie_IsUnauthorized()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>An expired refresh token is refused, and the dead cookie is cleared.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnExpiredRefreshToken_IsRejectedAndTheCookieCleared()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();

        var registration = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var session = await registration.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(session);

        // Age the token rather than waiting a week for it.
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            var token = await dbContext.Set<RefreshToken>()
                .SingleAsync(candidate => candidate.UserId == session.UserId);

            token.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(-1);
            await dbContext.SaveChangesAsync();
        }

        var response = await client.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The cookie is worthless now; leaving it would make the client retry with it forever.
        Assert.NotNull(ReadSetCookie(response));
    }

    /// <summary>Signing out kills the session: the same cookie no longer refreshes.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SigningOut_PreventsAnyFurtherRefresh()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        await this.SeedRolesAsync();

        using var client = factory.CreateClient();

        var registration = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var token = ReadCookieValue(registration);
        Assert.NotNull(token);

        var loggedOut = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);

        // Present the revoked token deliberately, rather than relying on the client having dropped it.
        using var retryClient = factory.CreateClient();

        var response = await retryClient.SendAsync(BuildRefreshRequest(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Signing out without a session is still a clean 204.</summary>
    /// <remarks>Sign-out is not a place to report that somebody's token was already invalid.</remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task SigningOutWithoutASession_IsStillNoContent()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>An expired access token opens nothing.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnExpiredAccessToken_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        ApplicationUser user;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            user = await TestData.SeedUserAsync(dbContext);
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateExpiredAccessToken(user, RoleNames.User));

        var response = await client.GetAsync(new Uri("/api/bookings/mine", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A token signed with somebody else's key is rejected, even when it claims to be an
    /// administrator.
    /// </summary>
    /// <remarks>
    /// Guards that signature validation is switched on at all. If it were not, anyone could mint
    /// themselves the administrator role, and every other test in this suite would still pass —
    /// they all present tokens this API signed itself.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ATokenSignedWithTheWrongKey_IsRejected()
    {
        await using var factory = new BookingApiFactory(this.fixture.ConnectionString);

        ApplicationUser user;
        await using (var dbContext = this.fixture.CreateDbContext())
        {
            user = await TestData.SeedUserAsync(dbContext);
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ForgeAdminToken(user));

        // The administrators-only listing: the endpoint a forged role claim would be aimed at.
        var response = await client.GetAsync(new Uri("/api/bookings", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Builds a well-formed administrator token signed with a key the API does not know.
    /// </summary>
    /// <remarks>
    /// Issuer, audience and claims all match what the API expects, so the only thing standing
    /// between this token and administrator access is the signature check.
    /// </remarks>
    /// <param name="user">The user to impersonate.</param>
    /// <returns>The forged JWT.</returns>
    private static string ForgeAdminToken(ApplicationUser user)
    {
        var attackerKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("an-attackers-key-that-the-api-never-configured"));

        var token = new JwtSecurityToken(
            issuer: "https://meetingrooms.tests",
            audience: "https://meetingrooms.tests",
            claims:
            [
                new Claim(ClaimNames.Subject, user.Id.ToString()),
                new Claim(ClaimNames.Name, user.DisplayName),
                new Claim(ClaimNames.Role, RoleNames.Admin),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(attackerKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Reads the parts of a problem-details response that describe the failure itself.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes <c>traceId</c>, which is generated per request and would make any two
    /// responses differ regardless of what they say.
    /// </remarks>
    /// <param name="response">The response to read.</param>
    /// <returns>The status, title and detail, rendered for comparison.</returns>
    private static async Task<string> ReadProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        return $"{problem?.Status}|{problem?.Title}|{problem?.Detail}";
    }

    /// <summary>Builds a registration request for a fresh account.</summary>
    /// <returns>The request.</returns>
    private static RegisterRequest NewRegistration()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];

        return new RegisterRequest(
            $"new-{suffix}@meetingrooms.test",
            $"New User {suffix[..4]}",
            "Str0ngPassword!");
    }

    /// <summary>Registers a new account and returns the session.</summary>
    /// <param name="client">The client to register through.</param>
    /// <returns>The session.</returns>
    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var session = await response.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.NotNull(session);

        return session;
    }

    /// <summary>Builds a refresh request carrying a specific cookie value.</summary>
    /// <param name="token">The raw refresh token to present.</param>
    /// <returns>The request.</returns>
    private static HttpRequestMessage BuildRefreshRequest(string token)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/auth/refresh", UriKind.Relative));

        request.Headers.Add("Cookie", $"{RefreshTokenCookie.Name}={token}");

        return request;
    }

    /// <summary>Finds the refresh cookie among the response's Set-Cookie headers.</summary>
    /// <param name="response">The response to inspect.</param>
    /// <returns>The raw header value, or <see langword="null"/>.</returns>
    private static string? ReadSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value =>
                value.StartsWith($"{RefreshTokenCookie.Name}=", StringComparison.Ordinal))
            : null;

    /// <summary>Extracts the refresh token value from a response's Set-Cookie header.</summary>
    /// <param name="response">The response to inspect.</param>
    /// <returns>The raw token, or <see langword="null"/>.</returns>
    private static string? ReadCookieValue(HttpResponseMessage response)
    {
        var cookie = ReadSetCookie(response);

        if (cookie is null)
        {
            return null;
        }

        var value = cookie[(cookie.IndexOf('=', StringComparison.Ordinal) + 1)..];
        var end = value.IndexOf(';', StringComparison.Ordinal);

        return end < 0 ? value : value[..end];
    }

    /// <summary>Creates the roles the API assigns on registration.</summary>
    /// <returns>A task that completes when the roles exist.</returns>
    private async Task SeedRolesAsync()
    {
        await using var dbContext = this.fixture.CreateDbContext();
        await TestData.SeedRolesAsync(dbContext);
    }
}
