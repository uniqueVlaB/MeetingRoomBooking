using System.ComponentModel.DataAnnotations;

namespace MeetingRooms.Api.Auth;

/// <summary>Settings for the access tokens the API issues and validates.</summary>
/// <remarks>
/// Validated on start-up rather than on first use, so a missing signing key stops the process
/// immediately instead of failing the first sign-in attempt in production.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Token issuer, echoed in the <c>iss</c> claim and checked on validation.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Intended audience, echoed in the <c>aud</c> claim and checked on validation.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Symmetric signing key.
    /// </summary>
    /// <remarks>
    /// At least 32 characters, because HMAC-SHA256 requires a 256-bit key and a shorter one throws
    /// at signing time. Never committed: supplied by user secrets locally and by Web App settings in
    /// Azure.
    /// </remarks>
    [Required]
    [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters for HMAC-SHA256.")]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Access-token lifetime in minutes.
    /// </summary>
    /// <remarks>
    /// Deliberately short. Access tokens cannot be revoked once issued, so the refresh token — which
    /// can be — carries the long-lived part of the session.
    /// </remarks>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;
}
