# Quick Start: Enabling Windows Authentication

This guide provides step-by-step instructions for enabling Windows authentication with AD group authorization.

## Prerequisites

- Windows Server or Windows 10/11 (domain-joined recommended)
- Active Directory access
- Application running on IIS or Kestrel

## Step 1: Identify Your AD Groups

Determine which Active Directory groups should have access to the application. You can list groups using PowerShell:

```powershell
# List all AD groups (requires AD PowerShell module)
Get-ADGroup -Filter * | Select-Object Name

# List groups for a specific user
Get-ADPrincipalGroupMembership username | Select-Object Name
```

## Step 2: Update Configuration

Edit `appsettings.json` or `appsettings.Production.json`:

```json
{
  "Authorization": {
    "EnableWindowsAuthentication": true,
    "AllowedActiveDirectoryGroups": [
      "YOURDOMAIN\\YourGroupName",
      "YOURDOMAIN\\AnotherGroupName"
    ],
    "RequireGroupMembership": true
  }
}
```

**Replace** `YOURDOMAIN` with your actual domain name and `YourGroupName` with your group names.

## Step 3: Configure IIS (if using IIS)

1. Open IIS Manager
2. Select your application
3. Double-click "Authentication"
4. **Enable** "Windows Authentication"
5. **Disable** "Anonymous Authentication"

## Step 4: Test the Configuration

1. Restart the application
2. Navigate to the application URL
3. You should be prompted for Windows credentials (or automatically authenticated if on a domain-joined machine)
4. Check the application logs for authorization messages

## Step 5: Verify Access Control

Test with different user accounts:

```powershell
# Check if a user is in a specific group
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
$principal.IsInRole("DOMAIN\GroupName")
```

## Common Scenarios

### Scenario 1: Allow All Authenticated Users

```json
{
  "Authorization": {
    "EnableWindowsAuthentication": true,
    "AllowedActiveDirectoryGroups": [],
    "RequireGroupMembership": false
  }
}
```

### Scenario 2: Multiple Groups with Different Permissions

```json
{
  "Authorization": {
    "EnableWindowsAuthentication": true,
    "AllowedActiveDirectoryGroups": [
      "CONTOSO\\Engineers",
      "CONTOSO\\QA-Team",
      "CONTOSO\\IT-Support",
      "BUILTIN\\Administrators"
    ],
    "RequireGroupMembership": true
  }
}
```

### Scenario 3: Development (No Authentication)

```json
{
  "Authorization": {
    "EnableWindowsAuthentication": false
  }
}
```

## Troubleshooting

### Problem: 401 Unauthorized

**Solution**: 
- Verify Windows Authentication is enabled in IIS
- Check the user is in one of the allowed groups
- Review application logs for authorization failures

### Problem: Prompt for credentials appears repeatedly

**Solution**:
- Ensure the browser supports Windows authentication
- Add the site to IE/Edge's Intranet zone or Trusted sites
- For Chrome: `chrome://flags/#auth-server-whitelist`

### Problem: Group membership check fails

**Solution**:
- Verify the group name format: `DOMAIN\GroupName`
- Check the domain name is correct
- Ensure the application server can reach the domain controller

## Security Best Practices

1. ✅ Use specific AD groups rather than allowing all users
2. ✅ Combine with `AllowedRoots` to restrict filesystem access
3. ✅ Review group membership regularly
4. ✅ Enable logging to monitor access attempts
5. ✅ Test with both member and non-member accounts

## Need Help?

See the full documentation in `docs/WINDOWS_AUTHENTICATION.md` for more details, including:
- Detailed configuration options
- Kestrel-specific setup
- Advanced troubleshooting
- Security recommendations
