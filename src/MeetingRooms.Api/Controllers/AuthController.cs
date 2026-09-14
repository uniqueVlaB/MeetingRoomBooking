using MeetingRooms.Api.Auth;
using MeetingRooms.Contracts.Auth;
using MeetingRooms.Core.Abstractions;
using MeetingRooms.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api.Controllers;

/// <summary>Registration, sign-in, session refresh and sign-out.</summary>
/// <remarks>
/// The session is split in two on purpose. The access token is short-lived, returned in the response
/// body and held in memory by the client. The refresh token is long-lived and travels only in an
/// HttpOnly cookie, so JavaScript cannot read it and a successful XSS cannot steal a durable
/// session.
/// </remarks>
/// <param name="userManager">Identity user store.</param>
/// <param name="tokenService">Issues access tokens.</param>
/// <param name="refreshTokenService">Issues, rotates and revokes refresh tokens.</param>
/// <param name="refreshTokenCookie">Reads and writes the refresh-token cookie.</param>
/// <param name="logger">Records registration failures that the caller is not shown.</param>
[ApiController]
[Route("api/auth")]
[Tags("Authentication")]
[AllowAnonymous]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    RefreshTokenCookie refreshTokenCookie,
    ILogger<AuthController> logger) : ControllerBase
{
    private readonly UserManager<ApplicationUser> userManager = userManager;
    private readonly ITokenService tokenService = tokenService;
    private readonly IRefreshTokenService refreshTokenService = refreshTokenService;
    private readonly RefreshTokenCookie refreshTokenCookie = refreshTokenCookie;
    private readonly ILogger<AuthController> logger = logger;

    /// <summary>Creates an account and signs it in.</summary>
    /// <param name="request">The account to create.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A new session.</returns>
    [HttpPost("register")]
    [EndpointSummary("Register")]
    [EndpointDescription("Creates an ordinary user account and starts a session.")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> RegisterAsync(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName,
        };

        var created = await this.userManager.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            return this.Problem(
                detail: string.Join(" ", created.Errors.Select(error => error.Description)),
                statusCode: StatusCodes.Status400BadRequest,
                title: "Registration failed");
        }

        // Everyone who registers is an ordinary user. Administrators are made by seeding or by an
        // existing administrator, never by self-service, which would make the role meaningless.
        var granted = await this.userManager.AddToRoleAsync(user, RoleNames.User);

        if (!granted.Succeeded)
        {
            // An account with no role authenticates but is refused everywhere, and the symptom --
            // "signed in, but nothing works" -- is tedious to trace back to here. Undo the account
            // instead, so the user can simply try again rather than being stuck with a broken one.
            this.logger.LogError(
                "Could not grant the {Role} role to a new account; the account was removed. {Errors}",
                RoleNames.User,
                string.Join("; ", granted.Errors.Select(error => error.Description)));

            await this.userManager.DeleteAsync(user);

            return this.Problem(
                detail: "The account could not be created. Please try again.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Registration failed");
        }

        return await this.IssueSessionAsync(user, cancellationToken);
    }

    /// <summary>Signs in with an email and password.</summary>
    /// <param name="request">The credentials.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A new session.</returns>
    [HttpPost("login")]
    [EndpointSummary("Sign in")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await this.userManager.FindByEmailAsync(request.Email);

        if (user is null || !await this.userManager.CheckPasswordAsync(user, request.Password))
        {
            // One message for both "no such account" and "wrong password", so the endpoint cannot be
            // used to discover which email addresses are registered.
            return this.Problem(
                detail: "Incorrect email address or password.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Sign-in failed");
        }

        return await this.IssueSessionAsync(user, cancellationToken);
    }

    /// <summary>Exchanges the refresh cookie for a fresh access token.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A new session, or 401 when the cookie is missing, expired or already used.</returns>
    [HttpPost("refresh")]
    [EndpointSummary("Refresh session")]
    [EndpointDescription("Rotates the refresh cookie and returns a new access token.")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> RefreshAsync(CancellationToken cancellationToken)
    {
        var presented = RefreshTokenCookie.Read(this.Request);

        if (presented is null)
        {
            return this.RefreshFailed();
        }

        var rotation = await this.refreshTokenService.RotateAsync(presented, cancellationToken);

        if (!rotation.IsSuccess || rotation.Value is null)
        {
            // Clear the cookie: it is worthless now, and leaving it in place means the client keeps
            // retrying with it on every page load.
            this.refreshTokenCookie.Clear(this.Response);
            return this.RefreshFailed();
        }

        var user = await this.userManager.FindByIdAsync(rotation.Value.UserId.ToString());

        if (user is null)
        {
            this.refreshTokenCookie.Clear(this.Response);
            return this.RefreshFailed();
        }

        this.refreshTokenCookie.Write(this.Response, rotation.Value.Token, rotation.Value.ExpiresUtc);

        return await this.BuildResponseAsync(user);
    }

    /// <summary>Ends the session by revoking the refresh token and clearing the cookie.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>No content.</returns>
    [HttpPost("logout")]
    [EndpointSummary("Sign out")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        var presented = RefreshTokenCookie.Read(this.Request);

        if (presented is not null)
        {
            await this.refreshTokenService.RevokeAsync(presented, cancellationToken);
        }

        this.refreshTokenCookie.Clear(this.Response);

        // Always 204, whether or not there was a session: signing out is not a place to report that
        // somebody's token was already invalid.
        return this.NoContent();
    }

    /// <summary>Issues both tokens and sets the refresh cookie.</summary>
    /// <param name="user">The signed-in user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The session.</returns>
    private async Task<ActionResult<AuthResponse>> IssueSessionAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var refresh = await this.refreshTokenService.IssueAsync(user.Id, cancellationToken);
        this.refreshTokenCookie.Write(this.Response, refresh.Token, refresh.ExpiresUtc);

        return await this.BuildResponseAsync(user);
    }

    /// <summary>Builds the session body, including the user's roles.</summary>
    /// <param name="user">The signed-in user.</param>
    /// <returns>The session.</returns>
    private async Task<ActionResult<AuthResponse>> BuildResponseAsync(ApplicationUser user)
    {
        var roles = await this.userManager.GetRolesAsync(user);
        var accessToken = this.tokenService.CreateAccessToken(user, roles);

        return this.Ok(new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresUtc,
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            [.. roles]));
    }

    /// <summary>The single failure response for refresh, so no variant leaks why it failed.</summary>
    /// <returns>A 401 problem-details response.</returns>
    private ObjectResult RefreshFailed() => this.Problem(
        detail: "That session has expired. Please sign in again.",
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Session expired");
}
