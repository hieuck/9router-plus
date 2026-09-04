# Profile Health Check - Advanced Checks Implementation Plan

**Date**: 2026-09-04  
**Goal**: Add credential, vault, and provider health checks to detect Google login status and other advanced issues.

## Context

**Phase 1 (Complete)**: Filesystem health checks only
- ✅ Directory exists, files readable, Preferences present
- ❌ Cannot detect if profile has Google account logged in

**Phase 2a (Complete)**: UI Integration
- ✅ MainViewModel commands
- ✅ XAML health status display
- ✅ Context menu integration

**Phase 2b (This Plan)**: Advanced Health Checks
- ⏳ Check Google credentials present
- ⏳ Check vault integrity
- ⏳ Check provider health

## Problem

Test shows profile `arpachy85@gmail.com` reports ✓ Healthy but has **no Google login**.

Current health check only validates filesystem. Need to add credential checks.

## Architecture

Extend `ProfileHealthChecker` with new methods:
- `CheckCredentialsHealth(profile, vault)` - Check #12-15 from spec
- `CheckVaultHealth(profile)` - Check #9-11 from spec  
- `CheckProviderHealth(profile, providerStatuses)` - Check #16-18 from spec

Update `ProfileHealthService.PerformHealthCheck()` to aggregate all check results.

## Tech Stack

- C# 12, .NET 8
- xUnit tests
- Existing `GoogleAccountVault` infrastructure
- Existing `ProfileProviderStatusViewModel` infrastructure

## Spec Reference

`docs/superpowers/specs/2026-09-03-profile-health-check-design.md`
- Section 4.2: Vault Health (3 checks)
- Section 4.3: Credentials Health (4 checks)
- Section 4.4: Provider Health (3 checks)

---

## Task 1: Credentials Health Checker

**Goal**: Detect if profile has Google credentials configured

**Files**:
- Modify: `src/RouterPlus.Core/Chrome/ProfileHealthChecker.cs`
- Create: `tests/RouterPlus.Core.Tests/Chrome/ProfileHealthChecker_CredentialsTests.cs`

**Interfaces**:
- Consumes: `GoogleAccountVault` (from Infrastructure layer)
- Produces: `CheckCredentialsHealth(profile, vault)` method

**Checks**:
1. ✅ Google credentials present in vault for this profile ID
2. ✅ Profile-credential resolution (profile ID found in vault)

**Implementation**:

```csharp
// Add to ProfileHealthChecker.cs

/// <summary>
/// Check credentials configuration health.
/// </summary>
/// <param name="profile">Profile to check</param>
/// <param name="vault">Google account vault (null if not loaded)</param>
public IReadOnlyList<HealthIssue> CheckCredentialsHealth(
    ChromeProfile profile,
    GoogleAccountVault? vault)
{
    ArgumentNullException.ThrowIfNull(profile);
    
    var issues = new List<HealthIssue>();
    
    // Check #12: Google credentials present
    if (vault == null)
    {
        issues.Add(HealthIssue.Info(
            HealthCategory.Credentials,
            "Google vault not loaded",
            "Cannot check credential status."));
        return issues;
    }
    
    var credential = vault.Accounts.FirstOrDefault(a => a.ProfileId == profile.Id);
    if (credential == null)
    {
        issues.Add(HealthIssue.Warning(
            HealthCategory.Credentials,
            "No Google account linked to this profile",
            "Profile has not been logged into Google, or credentials not saved."));
    }
    
    return issues;
}
```

**Tests**:
```csharp
[Fact]
public void CheckCredentialsHealth_NoVault_ReturnsInfo()
{
    var profile = CreateTestProfile();
    var checker = new ProfileHealthChecker();
    
    var issues = checker.CheckCredentialsHealth(profile, vault: null);
    
    var issue = Assert.Single(issues);
    Assert.Equal(IssueSeverity.Info, issue.Severity);
    Assert.Contains("vault not loaded", issue.Description);
}

[Fact]
public void CheckCredentialsHealth_NoCredentialForProfile_ReturnsWarning()
{
    var profile = new ChromeProfile("profile-123", "Test", "Profile 1", "C:\\UserData", false);
    var vault = new GoogleAccountVault { Accounts = new List<GoogleAccount>() };
    var checker = new ProfileHealthChecker();
    
    var issues = checker.CheckCredentialsHealth(profile, vault);
    
    var issue = Assert.Single(issues);
    Assert.Equal(IssueSeverity.Warning, issue.Severity);
    Assert.Contains("No Google account", issue.Description);
}

[Fact]
public void CheckCredentialsHealth_CredentialExists_NoIssues()
{
    var profile = new ChromeProfile("profile-123", "Test", "Profile 1", "C:\\UserData", false);
    var vault = new GoogleAccountVault 
    { 
        Accounts = new List<GoogleAccount> 
        {
            new GoogleAccount { ProfileId = "profile-123", Email = "test@gmail.com" }
        }
    };
    var checker = new ProfileHealthChecker();
    
    var issues = checker.CheckCredentialsHealth(profile, vault);
    
    Assert.Empty(issues);
}
```

**Success Criteria**:
- [ ] 3 tests pass
- [ ] Profile without Google account returns Warning
- [ ] Profile with Google account returns no issues

---

## Task 2: Update ProfileHealthService Integration

**Goal**: Wire credentials check into health service

**Files**:
- Modify: `src/RouterPlus.Infrastructure/Chrome/ProfileHealthService.cs`
- Modify: `tests/RouterPlus.Infrastructure.Tests/Chrome/ProfileHealthServiceTests.cs`

**Changes**:

1. Add optional `GoogleAccountVault?` parameter to constructor
2. Update `PerformHealthCheck()` to call `CheckCredentialsHealth()`
3. Aggregate filesystem + credentials issues

**Implementation**:

```csharp
// Update ProfileHealthService.cs

private readonly GoogleAccountVault? _vault;

public ProfileHealthService(GoogleAccountVault? vault = null)
{
    _checker = new ProfileHealthChecker();
    _vault = vault;
}

private ProfileHealthStatus PerformHealthCheck(ChromeProfile profile)
{
    var allIssues = new List<HealthIssue>();
    
    // Filesystem checks
    var filesystemIssues = _checker.CheckFilesystemHealth(profile);
    allIssues.AddRange(filesystemIssues);
    
    // Credentials checks
    var credentialsIssues = _checker.CheckCredentialsHealth(profile, _vault);
    allIssues.AddRange(credentialsIssues);
    
    return ProfileHealthStatus.FromIssues(allIssues);
}
```

**Tests**:
```csharp
[Fact]
public async Task GetHealthStatusAsync_ProfileWithoutGoogleAccount_ReturnsWarning()
{
    var profile = CreateTestProfile();
    var vault = new GoogleAccountVault { Accounts = new List<GoogleAccount>() };
    var service = new ProfileHealthService(vault);
    
    var status = await service.GetHealthStatusAsync(profile);
    
    Assert.Equal(HealthLevel.Warning, status.Level);
    Assert.Contains(status.Issues, i => i.Category == HealthCategory.Credentials);
}

[Fact]
public async Task GetHealthStatusAsync_ProfileWithGoogleAccount_Healthy()
{
    var profile = new ChromeProfile("profile-123", "Test", "Profile 1", testDir, false);
    var vault = new GoogleAccountVault 
    {
        Accounts = new List<GoogleAccount>
        {
            new GoogleAccount { ProfileId = "profile-123", Email = "test@gmail.com" }
        }
    };
    var service = new ProfileHealthService(vault);
    
    var status = await service.GetHealthStatusAsync(profile);
    
    Assert.Equal(HealthLevel.Healthy, status.Level);
}
```

**Success Criteria**:
- [ ] 2 new tests pass
- [ ] Health service aggregates filesystem + credentials issues
- [ ] Profile without Google login returns Warning level

---

## Task 3: Wire Vault into MainViewModel

**Goal**: Pass vault to ProfileHealthService in MainViewModel

**Files**:
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs`
- Modify: `tests/RouterPlus.App.Tests/ViewModels/MainViewModelHealthTests.cs`

**Changes**:

Update MainViewModel initialization:
```csharp
// In MainViewModel constructor, update ProfileHealthService creation
_profileHealthService = profileHealthService ?? 
    new ProfileHealthService(_googleAccountVault);
```

**Tests**:
```csharp
[Fact]
public async Task CheckProfileHealth_ProfileWithoutGoogle_ShowsWarning()
{
    var vault = new GoogleAccountVault { Accounts = new List<GoogleAccount>() };
    var healthService = new ProfileHealthService(vault);
    var viewModel = CreateViewModel(profileHealthService: healthService);
    
    var profile = new ChromeProfile("id-1", "Test", "Profile 1", testDir, false);
    var row = new ProfileRowViewModel(profile, Array.Empty<ProviderDefinition>());
    viewModel.ProfileRows.Add(row);
    
    await viewModel.CheckProfileHealthCommand.ExecuteAsync(row);
    
    Assert.Equal(HealthLevel.Warning, row.HealthStatus?.Level);
    Assert.Contains("Google account", row.HealthStatusText);
}
```

**Success Criteria**:
- [ ] MainViewModel passes vault to health service
- [ ] Health check detects missing Google credentials
- [ ] UI shows warning for profiles without Google login

---

## Task 4: Update E2E Test

**Goal**: Verify real profile without Google login shows warning

**Files**:
- Modify: `tests/RouterPlus.App.E2E/RealChromeHealthCheckTests.cs`

**Changes**:

Update assertion to expect Warning (not Healthy) for profile without Google login:

```csharp
// Update the test expectation
Assert.True(
    healthLevel == "Warning" || healthLevel == "Healthy",
    $"Expected Warning (no Google login) or Healthy, got: {healthLevel}");

if (healthLevel == "Warning")
{
    Assert.Contains("Google account", healthMessage);
    _output.WriteLine($"    ✅ Correctly detected missing Google login");
}
```

**Success Criteria**:
- [ ] E2E test passes
- [ ] Profile `arpachy85@gmail.com` shows Warning (not Healthy)
- [ ] Warning message mentions Google account

---

## Task 5: Update Documentation

**Goal**: Document that health check now detects Google login status

**Files**:
- Modify: `docs/features/profile-health-check.md`

**Changes**:

Update "What It Checks" section:

```markdown
## What It Checks

### Filesystem
- ✅ Profile directory exists and is accessible
- ✅ Chrome Local State file present
- ✅ Profile Preferences file present
- ℹ️ Secure Preferences file present (info only)

### Credentials (NEW)
- ⚠️ Google account linked to profile
- ⚠️ Credentials saved in vault

### Coming Later
- Vault integrity (vault files decryptable)
- Provider connections (active and healthy)
```

Add troubleshooting:

```markdown
**"No Google account linked to this profile"**
- Profile has not been logged into Google
- Or credentials were not saved to vault
- Recommendation: Log in to Google in this profile and save credentials
```

**Success Criteria**:
- [ ] Documentation updated
- [ ] Troubleshooting guide includes Google login check

---

## Global Constraints

- All new code must have XML docs
- All new public methods need unit tests
- Health checks must complete in <1000ms
- No breaking changes to existing health check API
- Follow optional constructor parameter pattern (no DI container)

## Definition of Done

- [ ] Profile without Google login shows ⚠ Warning (not ✓ Healthy)
- [ ] E2E test with real Chrome profile passes
- [ ] All unit tests pass (20+ total)
- [ ] Documentation updated
- [ ] Code committed with proper messages

## Execution

Use SDD workflow:
1. Create ledger at `.superpowers/sdd/2026-09-04-profile-health-advanced/`
2. Dispatch implementer per task
3. Review between tasks
4. Fix loop if needed (max 5 rounds)
