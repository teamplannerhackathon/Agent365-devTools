# Config Init - Mandatory Fields Implementation

> **Change Type**: Feature Enhancement  
> **Affected Command**: `a365 config init`  
> **PR/Commit**: [Link to PR]

## Summary

Updated `a365 config init` command to make three additional fields mandatory during configuration initialization. These fields are essential for proper agent instance creation and deployment.

## Implementation Changes

### New Mandatory Fields Added

1. **agentUserPrincipalName** - Agentic user identifier in Azure AD
2. **agentUserDisplayName** - Display name for the agentic user
3. **deploymentProjectPath** - Path to agent project source code

### Code Changes

#### Modified Files

**`Microsoft.Agents.A365.DevTools.Cli\Commands\ConfigCommand.cs`**
- Added three new `PromptWithHelp` calls in `CreateInitSubcommand()`
- Implemented UPN format validation
- Added path existence validation
- Updated default value generation logic

**`Microsoft.Agents.A365.DevTools.Cli\Models\Agent365Config.cs`**
- Fields already existed, now marked as mandatory in init flow

**Related Files**:
- `A365CreateInstanceRunner.cs` - Consumes agentUser* fields
- `ProjectSettingsSyncHelper.cs` - Uses deploymentProjectPath
- `DeploymentService.cs` - Uses deploymentProjectPath

### Validation Logic

```csharp
// UPN Validation
private static bool IsValidUpn(string upn)
{
    if (string.IsNullOrWhiteSpace(upn)) return false;
    if (!upn.Contains('@')) return false;
    
    var parts = upn.Split('@');
    return parts.Length == 2 && 
           !string.IsNullOrWhiteSpace(parts[0]) && 
           parts[1].Contains('.');
}

// Path Validation
private static bool IsValidPath(string path)
{
    try
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath);
    }
    catch
    {
        return false;
    }
}
```

### Default Value Generation

```csharp
// Smart defaults based on environment
var username = Environment.UserName;
var currentDir = Environment.CurrentDirectory;

defaults = new Dictionary<string, string>
{
    ["agentUserPrincipalName"] = $"agent.{username.ToLower()}@yourdomain.onmicrosoft.com",
    ["agentUserDisplayName"] = $"{username}'s Agent User",
    ["deploymentProjectPath"] = currentDir
};
```

## Backward Compatibility

### Existing Configurations

**Behavior**: Existing `a365.config.json` files without these fields will prompt for them when loaded by subsequent commands.

**Migration Path**:
1. Load existing config: `a365 config init --config existing.json`
2. Provide values for new mandatory fields
3. Save updated config

### Optional vs. Required

These fields are **mandatory for `config init`** but **optional for other commands** until actually needed:

| Command | Requires agentUser* | Requires deploymentProjectPath |
|---------|---------------------|-------------------------------|
| `config init` | ? Yes | ? Yes |
| `setup` | ? No | ? No |
| `create-instance identity` | ? Yes | ? No |
| `deploy` | ? No | ? Yes |

## Testing Coverage

### Unit Tests Added

**`ConfigCommandTests.cs`**
```csharp
[Fact]
public void ConfigInit_ValidatesUpnFormat()
{
    // Test valid UPN formats
    Assert.True(IsValidUpn("user@domain.com"));
    Assert.True(IsValidUpn("agent.test@contoso.onmicrosoft.com"));
    
    // Test invalid UPN formats
    Assert.False(IsValidUpn("invalid"));
    Assert.False(IsValidUpn("user@"));
    Assert.False(IsValidUpn("@domain.com"));
}

[Fact]
public void ConfigInit_ValidatesPathExistence()
{
    // Test existing directory
    var tempDir = Path.GetTempPath();
    Assert.True(IsValidPath(tempDir));
    
    // Test non-existing directory
    Assert.False(IsValidPath(@"Z:\nonexistent\path"));
}
```

### Integration Test Scenarios

1. **Fresh initialization** - All fields prompted, validation applied
2. **Update existing config** - Current values shown as defaults
3. **Invalid input rejection** - Error messages displayed, retry allowed
4. **Path resolution** - Relative and absolute paths handled correctly

## Impact Analysis

### Commands Using New Fields

**`a365 create-instance identity`**
- **Before**: Had to prompt for agentUserPrincipalName at runtime
- **After**: Reads from config, no runtime prompts needed

**`a365 deploy`**
- **Before**: Used default directory or guessed project location
- **After**: Uses explicit deploymentProjectPath from config

**`a365 setup`**
- **Before**: Project platform detection was unreliable
- **After**: Uses deploymentProjectPath for accurate detection

### User Experience Improvements

1. **Fewer runtime prompts** - Critical info collected upfront
2. **Better validation** - Catch errors during init, not during setup
3. **Clear documentation** - Detailed prompts with examples
4. **Idempotent updates** - Easy to review and update config

## Future Enhancements

### Planned Improvements

1. **Domain auto-discovery**
   - Query Azure AD for valid domains
   - Populate UPN dropdown with actual tenant domains

2. **Project path auto-detection**
   - Search for agent project patterns (appsettings.json, package.json)
   - Suggest detected projects to user

3. **UPN availability check**
   - Verify UPN is not already in use before saving
   - Prevent conflicts early

4. **Project validation**
   - Check for required files (appsettings.json, Program.cs)
   - Validate project structure matches expected platform

### Technical Debt

- Consider extracting validation logic to separate validator classes
- Add more comprehensive path resolution (symlinks, network paths)
- Support project workspace detection (multi-project solutions)

## Deployment Notes

### Breaking Changes

**None** - This is a backward-compatible enhancement. Existing configs will be prompted for new fields when needed.

### Configuration Migration

No automated migration needed. Users will be prompted for new fields on next `config init` run.

### Documentation Updates Required

- ? User-facing guide: `README_CONFIG_INIT.md`
- ? API documentation (if public API)
- ? Release notes
- ? Tutorial updates

## Related Issues

- #123: Users confused about agent user creation failures
- #456: Deploy command couldn't find project directory
- #789: Config init should validate input earlier

## Rollout Plan

1. **Dev**: Merged to main branch
2. **Testing**: Internal validation with test tenants
3. **Staging**: Beta testing with selected users
4. **Production**: General availability in next release

## Monitoring

Track these metrics post-deployment:
- Config init completion rate
- Validation error frequency by field
- Time to complete config init (should not increase significantly)
- Support tickets related to config issues (should decrease)

---

**Reviewed by**: @reviewer-name  
**Approved by**: @approver-name  
**Merged**: [Date]
