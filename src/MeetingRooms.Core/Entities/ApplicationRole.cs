using Microsoft.AspNetCore.Identity;

namespace MeetingRooms.Core.Entities;

/// <summary>A role, used to separate ordinary users from administrators.</summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>Creates an unnamed role, as required by the Identity stores.</summary>
    public ApplicationRole()
    {
    }

    /// <summary>Creates a role with the given name.</summary>
    /// <param name="roleName">The role name, from <see cref="RoleNames"/>.</param>
    public ApplicationRole(string roleName)
        : base(roleName)
    {
    }
}
