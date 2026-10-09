using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;

namespace FolderCompare.Api.Authorization;

/// <summary>
/// Authorization handler that verifies a Windows user is a member of at least one allowed Active Directory group.
/// </summary>
public sealed class ActiveDirectoryGroupHandler : AuthorizationHandler<ActiveDirectoryGroupRequirement>
{
    private readonly ILogger<ActiveDirectoryGroupHandler> _logger;

    public ActiveDirectoryGroupHandler(ILogger<ActiveDirectoryGroupHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveDirectoryGroupRequirement requirement)
    {
        // If group membership is not required, succeed for any authenticated user
        if (!requirement.RequireGroupMembership || requirement.AllowedGroups.Count == 0)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                _logger.LogDebug("Group membership not required; allowing authenticated user {User}",
                    context.User.Identity.Name);
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning("User is not authenticated");
            }
            return Task.CompletedTask;
        }

        var identity = context.User.Identity as WindowsIdentity;
        if (identity == null || !identity.IsAuthenticated)
        {
            _logger.LogWarning("User is not authenticated with Windows authentication");
            return Task.CompletedTask;
        }

        var userName = identity.Name ?? "Unknown";
        _logger.LogDebug("Checking AD group membership for user {User}", userName);

        // Check if the user is a member of any allowed group
        foreach (var allowedGroup in requirement.AllowedGroups)
        {
            try
            {
                var principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(allowedGroup))
                {
                    _logger.LogInformation("User {User} is a member of allowed group {Group}",
                        userName, allowedGroup);
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking group membership for {Group}", allowedGroup);
            }
        }

        _logger.LogWarning("User {User} is not a member of any allowed AD groups", userName);
        return Task.CompletedTask;
    }
}
