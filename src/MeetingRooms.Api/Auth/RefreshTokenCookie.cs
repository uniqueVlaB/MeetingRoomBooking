using Microsoft.Extensions.Options;

namespace MeetingRooms.Api.Auth;

/// <summary>Settings governing how the refresh-token cookie is written.</summary>
public sealed class RefreshTokenCookieOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "RefreshTokenCookie";

    /// <summary>
    /// Whether the client is served from a different origin than the API.
    /// </summary>
    /// <remarks>
    /// This is the setting most likely to be wrong in a deployed environment. The client and API are
    /// deployed as two separate Azure Web Apps, so the cookie is cross-site and the browser will only
    /// store and return it when it is marked <c>SameSite=None</c> and <c>Secure</c>. Locally the
    /// Angular dev server proxies <c>/api</c>, making the cookie same-site, where <c>SameSite=None</c>
    /// over plain HTTP would be rejected instead. Hence a switch rather than a constant.
    /// </remarks>
    public bool CrossSite { get; set; }
}

/// <summary>Reads and writes the HttpOnly cookie carrying the refresh token.</summary>
/// <remarks>
/// The refresh token is kept in a cookie rather than in <c>localStorage</c> so that JavaScript
/// cannot read it, which means a successful XSS cannot walk away with a long-lived session.
/// </remarks>
/// <param name="options">Cookie behaviour for this environment.</param>
public sealed class RefreshTokenCookie(IOptions<RefreshTokenCookieOptions> options)
{
    /// <summary>The cookie name.</summary>
    public const string Name = "meetingrooms_refresh";

    private readonly RefreshTokenCookieOptions options = options.Value;

    /// <summary>Reads the refresh token from the request, if present.</summary>
    /// <param name="request">The incoming request.</param>
    /// <returns>The raw token, or <see langword="null"/>.</returns>
    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies.TryGetValue(Name, out var token) ? token : null;
    }

    /// <summary>Writes the refresh token to the response.</summary>
    /// <param name="response">The outgoing response.</param>
    /// <param name="token">The raw token.</param>
    /// <param name="expiresUtc">When the cookie should expire.</param>
    public void Write(HttpResponse response, string token, DateTimeOffset expiresUtc)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Append(Name, token, this.BuildOptions(expiresUtc));
    }

    /// <summary>Clears the refresh-token cookie.</summary>
    /// <param name="response">The outgoing response.</param>
    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Deleting must repeat the same attributes the cookie was written with, or the browser
        // treats it as a different cookie and quietly keeps the original.
        response.Cookies.Delete(Name, this.BuildOptions(DateTimeOffset.UnixEpoch));
    }

    /// <summary>Builds the cookie attributes for this environment.</summary>
    /// <param name="expiresUtc">Cookie expiry.</param>
    /// <returns>The cookie options.</returns>
    private CookieOptions BuildOptions(DateTimeOffset expiresUtc) => new()
    {
        // Not readable from JavaScript.
        HttpOnly = true,

        // Cross-site cookies must be Secure; browsers reject SameSite=None over plain HTTP.
        Secure = this.options.CrossSite,
        SameSite = this.options.CrossSite ? SameSiteMode.None : SameSiteMode.Lax,

        // Scoped to the refresh and sign-out endpoints, so it is not attached to every API call.
        Path = "/api/auth",
        Expires = expiresUtc,
    };
}
