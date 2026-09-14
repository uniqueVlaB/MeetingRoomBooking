using System.Security.Claims;
using MeetingRooms.Api.Auth;
using MeetingRooms.Core.Entities;
using Xunit;

namespace MeetingRooms.Tests.Auth;

/// <summary>
/// How the API decides who is calling.
/// </summary>
/// <remarks>
/// Every booking is attributed with <c>GetUserId</c>, and every administrator-only action turns on
/// <c>IsAdmin</c>. Both read raw claims, so they are worth testing directly rather than only through
/// endpoints that happen to present well-formed tokens.
/// </remarks>
public sealed class ClaimsPrincipalExtensionsTests
{
    /// <summary>The subject claim identifies the caller.</summary>
    [Fact]
    public void GetUserId_ReadsTheSubjectClaim()
    {
        var userId = Guid.CreateVersion7();
        var principal = PrincipalWith(new Claim(ClaimNames.Subject, userId.ToString()));

        Assert.Equal(userId, principal.GetUserId());
    }

    /// <summary>
    /// The standard name identifier is accepted when there is no short subject claim.
    /// </summary>
    /// <remarks>
    /// Inbound claim mapping is switched off, but a token minted elsewhere may still carry the long
    /// form. Accepting both is what stops that being an unexplained 500.
    /// </remarks>
    [Fact]
    public void GetUserId_FallsBackToTheNameIdentifierClaim()
    {
        var userId = Guid.CreateVersion7();
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        Assert.Equal(userId, principal.GetUserId());
    }

    /// <summary>
    /// A token with no usable subject is fatal rather than silently anonymous.
    /// </summary>
    /// <remarks>
    /// The alternative — returning <see cref="Guid.Empty"/> — would attribute bookings to a user
    /// that does not exist, and would look like working software until somebody asked whose booking
    /// it was.
    /// </remarks>
    [Fact]
    public void GetUserId_ThrowsWhenThereIsNoSubject()
    {
        var principal = PrincipalWith(new Claim(ClaimNames.Name, "Nameless"));

        Assert.Throws<InvalidOperationException>(() => principal.GetUserId());
    }

    /// <summary>A subject that is not an identifier at all is equally fatal.</summary>
    [Fact]
    public void GetUserId_ThrowsWhenTheSubjectIsNotAGuid()
    {
        var principal = PrincipalWith(new Claim(ClaimNames.Subject, "not-a-guid"));

        Assert.Throws<InvalidOperationException>(() => principal.GetUserId());
    }

    /// <summary>The administrator role is recognised.</summary>
    [Fact]
    public void IsAdmin_IsTrueForAnAdministrator()
    {
        var principal = PrincipalWith(
            new Claim(ClaimNames.Subject, Guid.CreateVersion7().ToString()),
            new Claim(ClaimNames.Role, RoleNames.Admin));

        Assert.True(principal.IsAdmin());
    }

    /// <summary>An ordinary user is not an administrator.</summary>
    [Fact]
    public void IsAdmin_IsFalseForAnOrdinaryUser()
    {
        var principal = PrincipalWith(
            new Claim(ClaimNames.Subject, Guid.CreateVersion7().ToString()),
            new Claim(ClaimNames.Role, RoleNames.User));

        Assert.False(principal.IsAdmin());
    }

    /// <summary>
    /// Builds a principal shaped the way the JWT handler produces one.
    /// </summary>
    /// <remarks>
    /// The role claim type is set explicitly to the short name the API issues, matching the
    /// handler's configuration. With the default type, role checks would read nothing and every
    /// administrator would look like an ordinary user.
    /// </remarks>
    /// <param name="claims">Claims the token carries.</param>
    /// <returns>The principal.</returns>
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: ClaimNames.Name,
            roleType: ClaimNames.Role));
}
