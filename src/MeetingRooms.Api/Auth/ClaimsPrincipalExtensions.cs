using System.Security.Claims;
using MeetingRooms.Core.Entities;

namespace MeetingRooms.Api.Auth;

/// <summary>Reads the identity of the caller from their token.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the caller's user identifier.
    /// </summary>
    /// <param name="principal">The authenticated caller.</param>
    /// <returns>The user's identifier.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the token carries no usable subject. This is deliberately fatal rather than
    /// returning <see cref="Guid.Empty"/>: a bookings endpoint that silently attributed a row to the
    /// empty user would be far harder to notice than a failed request.
    /// </exception>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var value = principal.FindFirstValue(ClaimNames.Subject)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException("The access token does not carry a valid user identifier.");
    }

    /// <summary>Whether the caller may manage rooms and act on other users' bookings.</summary>
    /// <param name="principal">The authenticated caller.</param>
    /// <returns><see langword="true"/> when the caller holds the administrator role.</returns>
    public static bool IsAdmin(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.IsInRole(RoleNames.Admin);
    }
}
