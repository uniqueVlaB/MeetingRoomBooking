using System.ComponentModel.DataAnnotations;

namespace MeetingRooms.Contracts.Auth;

/// <summary>Request to create an account.</summary>
/// <param name="Email">Email address, which doubles as the user name.</param>
/// <param name="DisplayName">Name shown next to the user's bookings.</param>
/// <param name="Password">Chosen password.</param>
public sealed record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, StringLength(128, MinimumLength = 2)] string DisplayName,
    [Required, StringLength(128, MinimumLength = 8)] string Password);

/// <summary>Request to sign in.</summary>
/// <param name="Email">The registered email address.</param>
/// <param name="Password">The account password.</param>
public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

/// <summary>
/// A signed-in session.
/// </summary>
/// <remarks>
/// Only the short-lived access token appears here. The refresh token is deliberately absent: it is
/// delivered as an HttpOnly cookie so that JavaScript — and therefore any successful XSS — cannot
/// read it.
/// </remarks>
/// <param name="AccessToken">Bearer token for API calls and the SignalR connection.</param>
/// <param name="ExpiresUtc">When the access token stops being accepted.</param>
/// <param name="UserId">The signed-in user's identifier.</param>
/// <param name="Email">The signed-in user's email address.</param>
/// <param name="DisplayName">The signed-in user's display name.</param>
/// <param name="Roles">Roles held by the user; drives what the UI offers.</param>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresUtc,
    Guid UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles);
