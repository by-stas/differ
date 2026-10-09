namespace FolderCompare.Api.Configuration;

public sealed class AuthorizationOptions
{
    public const string SectionName = "Authorization";

    /// <summary>Enable Windows authentication and Active Directory group-based authorization.</summary>
    public bool EnableWindowsAuthentication { get; set; } = false;

    /// <summary>
    /// Active Directory groups allowed to access the application.
    /// Users must be members of at least one of these groups.
    /// Format: Domain\GroupName or just GroupName for the local domain.
    /// Leave empty to allow all authenticated Windows users.
    /// </summary>
    public List<string> AllowedActiveDirectoryGroups { get; set; } = new();

    /// <summary>When true, requires the user to be a member of at least one allowed AD group.</summary>
    public bool RequireGroupMembership { get; set; } = true;
}
