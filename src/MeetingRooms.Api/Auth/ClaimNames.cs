namespace MeetingRooms.Api.Auth;

/// <summary>
/// The claim types this API issues and reads.
/// </summary>
/// <remarks>
/// Short JWT names rather than the long <c>ClaimTypes.*</c> URIs, because inbound claim mapping is
/// switched off (see <c>AuthConfiguration</c>). Turning mapping off means what the token says is
/// what the code sees — no silent rewriting of <c>sub</c> into a schemas.xmlsoap.org URI — but it
/// only works if issuing and reading agree, which is what these constants guarantee.
/// </remarks>
public static class ClaimNames
{
    /// <summary>The user's identifier.</summary>
    public const string Subject = "sub";

    /// <summary>The user's display name.</summary>
    public const string Name = "name";

    /// <summary>The user's email address.</summary>
    public const string Email = "email";

    /// <summary>A role held by the user.</summary>
    public const string Role = "role";

    /// <summary>Unique identifier for this token.</summary>
    public const string TokenId = "jti";
}
