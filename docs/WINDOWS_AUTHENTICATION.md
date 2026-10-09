# Windows Authentication with Active Directory Groups

This document explains how to configure Windows authentication and Active Directory group-based authorization for the Folder Compare application.

## Overview

The application supports Windows authentication using the Negotiate authentication scheme (Kerberos/NTLM). When enabled, all API endpoints require Windows authentication, and you can optionally restrict access to specific Active Directory groups.

## Configuration

All authorization settings are configured in `appsettings.json` or environment-specific configuration files (e.g., `appsettings.Production.json`).

### Basic Configuration

```json
{
  "Authorization": {
    "EnableWindowsAuthentication": true,
    "AllowedActiveDirectoryGroups": [
      "DOMAIN\\FolderCompare-Users",
      "DOMAIN\\IT-Admins"
    ],
    "RequireGroupMembership": true
  }
}
```

### Configuration Options

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `EnableWindowsAuthentication` | boolean | `false` | Enables Windows authentication and authorization |
| `AllowedActiveDirectoryGroups` | string[] | `[]` | List of AD groups allowed to access the API |
| `RequireGroupMembership` | boolean | `true` | Requires users to be members of at least one allowed group |

## Group Name Formats

AD group names can be specified in two formats:

1. **Fully qualified**: `DOMAIN\GroupName` (e.g., `CONTOSO\FolderCompare-Users`)
2. **Local domain**: `GroupName` (uses the local machine's domain)

Examples:
```json
"AllowedActiveDirectoryGroups": [
  "CONTOSO\\Engineering",
  "BUILTIN\\Administrators",
  "IT-Team"
]
```

## Access Control Behavior

### When `EnableWindowsAuthentication` is `false`
- No authentication required
- All endpoints are publicly accessible (localhost by default)

### When `EnableWindowsAuthentication` is `true`
- All API endpoints require Windows authentication
- The user's Windows identity is used for authentication

### With `RequireGroupMembership` set to `true` and non-empty `AllowedActiveDirectoryGroups`
- Users must be authenticated with Windows
- Users must be members of at least one specified AD group
- Access is denied if the user is not in any allowed group

### With `RequireGroupMembership` set to `false` or empty `AllowedActiveDirectoryGroups`
- Users must be authenticated with Windows
- Any authenticated Windows user is allowed

## IIS Configuration

When hosting on IIS, ensure Windows Authentication is enabled:

1. Open IIS Manager
2. Select your application
3. Open "Authentication"
4. Enable "Windows Authentication"
5. Disable "Anonymous Authentication"

In `web.config`:
```xml
<system.webServer>
  <security>
    <authentication>
      <anonymousAuthentication enabled="false" />
      <windowsAuthentication enabled="true" />
    </authentication>
  </security>
</system.webServer>
```

## Kestrel Configuration (Development)

For local development on Windows, the Negotiate authentication package automatically handles Windows authentication. No additional web server configuration is needed.

## Troubleshooting

### Users are denied access
1. Verify the user is authenticated with Windows credentials
2. Check the user is a member of at least one allowed AD group
3. Verify the group name format is correct (use `DOMAIN\GroupName`)
4. Check application logs for authorization failures

### Authentication not working
1. Ensure `EnableWindowsAuthentication` is set to `true`
2. Verify Windows Authentication is enabled in IIS (if using IIS)
3. Check the application is running on Windows (or a domain-joined machine)
4. Verify the `Microsoft.AspNetCore.Authentication.Negotiate` package is installed

### Testing Group Membership

You can verify a user's group membership using PowerShell:
```powershell
# Get current user's groups
[System.Security.Principal.WindowsIdentity]::GetCurrent().Groups | 
  ForEach-Object { $_.Translate([System.Security.Principal.NTAccount]) }

# Check if user is in a specific group
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
$principal.IsInRole("DOMAIN\GroupName")
```

## Security Recommendations

1. **Always enable `EnableWindowsAuthentication` in production** when deploying beyond localhost
2. **Use specific AD groups** rather than allowing all authenticated users
3. **Combine with `AllowedRoots`** to restrict filesystem access
4. **Review group membership regularly** to ensure only authorized users have access
5. **Use domain groups** (e.g., `DOMAIN\GroupName`) rather than local groups for better auditing

## Example Configurations

### Development (No Authentication)
```json
{
  "Authorization": {
    "EnableWindowsAuthentication": false
  }
}
```

### Production (Restricted to Specific Groups)
```json
{
  "Authorization": {
    "EnableWindowsAuthentication": true,
    "AllowedActiveDirectoryGroups": [
      "CONTOSO\\FolderCompare-Users",
      "CONTOSO\\Build-Engineers",
      "CONTOSO\\IT-Admins"
    ],
    "RequireGroupMembership": true
  },
  "Comparison": {
    "AllowedRoots": [
      "C:\\Builds",
      "\\\\FileServer\\Releases"
    ]
  }
}
```

### Production (All Authenticated Windows Users)
```json
{
  "Authorization": {
    "EnableWindowsAuthentication": true,
    "AllowedActiveDirectoryGroups": [],
    "RequireGroupMembership": false
  }
}
```

## Logging

The authorization handler logs the following events at different levels:

- **Debug**: Group membership checks and successful authorizations
- **Information**: Successful group matches
- **Warning**: Authentication failures and authorization denials
- **Error**: Exceptions during group membership checks

To enable detailed authorization logging, configure logging in `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "FolderCompare.Api.Authorization": "Debug"
    }
  }
}
```
