using Microsoft.AspNetCore.Authorization;

namespace FolderCompare.Api.Authorization;

/// <summary>
/// Authorization requirement that enforces Active Directory group membership.
/// </summary>
public sealed class ActiveDirectoryGroupRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> AllowedGroups { get; }
    public bool RequireGroupMembership { get; }

    public ActiveDirectoryGroupRequirement(IReadOnlyList<string> allowedGroups, bool requireGroupMembership)
    {
        AllowedGroups = allowedGroups;
        RequireGroupMembership = requireGroupMembership;
    }
}
