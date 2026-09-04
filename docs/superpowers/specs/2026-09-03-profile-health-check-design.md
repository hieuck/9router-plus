# Profile Health Check System - Design Specification

**Gate Context**:
- **Importers/Callers**: Implementation team (for coding the feature), user (requested to save design before implementation per "Lưu thiết kế và triển khai"), future maintainers
- **Affected API**: ProfileRowViewModel (adds HealthStatus property), MainViewModel (adds CheckAllProfilesHealthCommand), new ProfileHealthChecker (Core), new ProfileHealthService (Infrastructure)
- **Data Schemas**: ProfileHealthStatus (Level, Message, LastChecked, Issues), HealthIssue (Category, Severity, Description, Recommendation), enums: HealthLevel, HealthCategory, IssueSeverity
- **User's Verbatim Instruction**: "Lưu thiết kế và triển khai" (Save design and implement) and "Tốt. Lên kế hoạch và triển khai đầy đủ quy trình" (Good. Plan and implement full process)

---

**Date**: 2026-09-03  
**Status**: Approved for Implementation  
**Approach**: Smart Passive Monitoring (Approach C)

---

## 1. EXECUTIVE SUMMARY

### Problem Statement
Users need visibility into Chrome profile health to diagnose issues before they impact auto-login operations. Current system only tracks provider connection health, not the underlying profile filesystem, vault integrity, or credential configuration status.

### Solution Overview
Implement a **Smart Passive Monitoring** system that performs 19 health checks across 4 categories:
- **Passive checks (13)**: Zero-cost, always-on monitoring using existing data
- **Active checks (6)**: On-demand validation with 5-minute caching

### Key Benefits
1. **Proactive issue detection**: Identify filesystem/vault problems before they cause failures
2. **Actionable diagnostics**: Provide specific recommendations for each issue type
3. **Performance-conscious**: Passive checks are free, active checks are cached
4. **Minimal UI impact**: Integrates into existing profile list, no new tabs/views

### Success Metrics
- Users can identify profile health status in <2 seconds (visual icon)
- Health check overhead <100ms per profile (passive checks)
- 90% of common issues detected and surfaced with recommendations

---

## 2. ARCHITECTURE

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    RouterPlus.App Layer                     │
├─────────────────────────────────────────────────────────────┤
│  ProfileRowViewModel                                        │
│  ├── HealthStatus: ProfileHealthStatus                      │
│  ├── HealthStatusIcon: string (computed)                    │
│  ├── HealthStatusText: string (computed)                    │
│  └── CheckHealthCommand: ICommand                           │
├─────────────────────────────────────────────────────────────┤
│  MainViewModel                                              │
│  └── CheckAllProfilesHealthCommand: ICommand                │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│              RouterPlus.Infrastructure Layer                │
├─────────────────────────────────────────────────────────────┤
│  ProfileHealthService                                       │
│  ├── GetHealthStatusAsync(profile) → cached result         │
│  ├── RefreshHealthStatusAsync(profile) → force check       │
│  ├── CheckAllProfilesAsync() → batch operation             │
│  └── _cache: Dictionary<profileId, CachedHealthStatus>     │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│                 RouterPlus.Core Layer                       │
├─────────────────────────────────────────────────────────────┤
│  ProfileHealthChecker (stateless)                           │
│  ├── CheckFilesystemHealth(profile)                        │
│  ├── CheckVaultIntegrity(profile, vaultSession)            │
│  ├── CheckCredentialsConfig(profile, vault)                │
│  └── CheckProviderHealth(profile, connections)             │
└─────────────────────────────────────────────────────────────┘
```

### Layer Responsibilities

**Core Layer** (`RouterPlus.Core`):
- Stateless health checking logic
- Data models for health status and issues
- No caching, no external dependencies
- Pure functions: `ChromeProfile → HealthResult`

**Infrastructure Layer** (`RouterPlus.Infrastructure`):
- `ProfileHealthService` with caching strategy
- Orchestrates checks across filesystem, vault, credentials
- Manages cache invalidation (5-minute TTL, manual refresh)
- Handles exceptions from file I/O and vault operations

**App Layer** (`RouterPlus.App`):
- UI bindings in `ProfileRowViewModel`
- Commands for single/batch health checks
- Status icon/text presentation
- Health issue popover/tooltip

---

## 3. DATA MODELS

### 3.1 ProfileHealthStatus

**Location**: `RouterPlus.Core\Chrome\ProfileHealthStatus.cs`

```csharp
namespace RouterPlus.Core.Chrome;

/// <summary>
/// Health status result for a Chrome profile.
/// Aggregates checks across filesystem, vault, credentials, and providers.
/// </summary>
public sealed record ProfileHealthStatus
{
    /// <summary>
    /// Overall health level (Healthy/Warning/Error/Unknown).
    /// Computed from highest severity issue present.
    /// </summary>
    public HealthLevel Level { get; init; }

    /// <summary>
    /// Human-readable summary message.
    /// Example: "Profile accessible, 2 credentials configured"
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// When this health check was performed (UTC).
    /// </summary>
    public DateTime LastChecked { get; init; }

    /// <summary>
    /// Detailed issues found during health check.
    /// Empty if Level = Healthy.
    /// </summary>
    public IReadOnlyList<HealthIssue> Issues { get; init; } = Array.Empty<HealthIssue>();

    /// <summary>
    /// Create healthy status with no issues.
    /// </summary>
    public static ProfileHealthStatus Healthy(string message)
        => new()
        {
            Level = HealthLevel.Healthy,
            Message = message,
            LastChecked = DateTime.UtcNow,
            Issues = Array.Empty<HealthIssue>()
        };

    /// <summary>
    /// Create status from list of issues.
    /// Level computed from highest severity issue.
    /// </summary>
    public static ProfileHealthStatus FromIssues(IEnumerable<HealthIssue> issues)
    {
        var issueList = issues.ToArray();
        var level = ComputeHealthLevel(issueList);
        var message = FormatSummaryMessage(level, issueList);

        return new ProfileHealthStatus
        {
            Level = level,
            Message = message,
            LastChecked = DateTime.UtcNow,
            Issues = issueList
        };
    }

    private static HealthLevel ComputeHealthLevel(IReadOnlyList<HealthIssue> issues)
    {
        if (issues.Count == 0) return HealthLevel.Healthy;
        if (issues.Any(i => i.Severity == IssueSeverity.Error)) return HealthLevel.Error;
        if (issues.Any(i => i.Severity == IssueSeverity.Warning)) return HealthLevel.Warning;
        return HealthLevel.Healthy;
    }

    private static string FormatSummaryMessage(HealthLevel level, IReadOnlyList<HealthIssue> issues)
    {
        return level switch
        {
            HealthLevel.Healthy => "Profile healthy",
            HealthLevel.Warning => $"{issues.Count} warning(s) detected",
            HealthLevel.Error => $"{issues.Count(i => i.Severity == IssueSeverity.Error)} error(s) detected",
            HealthLevel.Unknown => "Health status unknown",
            _ => "Unknown status"
        };
    }
}

/// <summary>
/// Overall health level for a profile.
/// </summary>
public enum HealthLevel
{
    /// <summary>
    /// Health status has not been determined yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// All checks passed, no issues found.
    /// </summary>
    Healthy,

    /// <summary>
    /// Minor issues detected (non-critical).
    /// Profile may still be usable but needs attention.
    /// </summary>
    Warning,

    /// <summary>
    /// Critical issues detected.
    /// Profile likely unusable until resolved.
    /// </summary>
    Error
}
```

### 3.2 HealthIssue

**Location**: `RouterPlus.Core\Chrome\HealthIssue.cs`

```csharp
namespace RouterPlus.Core.Chrome;

/// <summary>
/// Represents a specific health issue found during profile health check.
/// </summary>
public sealed record HealthIssue
{
    /// <summary>
    /// Category of health check that found this issue.
    /// </summary>
    public HealthCategory Category { get; init; }

    /// <summary>
    /// Severity level of this issue.
    /// </summary>
    public IssueSeverity Severity { get; init; }

    /// <summary>
    /// Human-readable description of the issue.
    /// Example: "Profile directory not found"
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Optional recommendation for resolving the issue.
    /// Example: "Profile may have been deleted externally. Consider removing from catalog."
    /// </summary>
    public string? Recommendation { get; init; }

    /// <summary>
    /// Create an informational issue (severity: Info).
    /// </summary>
    public static HealthIssue Info(HealthCategory category, string description)
        => new() { Category = category, Severity = IssueSeverity.Info, Description = description };

    /// <summary>
    /// Create a warning issue (severity: Warning).
    /// </summary>
    public static HealthIssue Warning(HealthCategory category, string description, string? recommendation = null)
        => new() { Category = category, Severity = IssueSeverity.Warning, Description = description, Recommendation = recommendation };

    /// <summary>
    /// Create an error issue (severity: Error).
    /// </summary>
    public static HealthIssue Error(HealthCategory category, string description, string? recommendation = null)
        => new() { Category = category, Severity = IssueSeverity.Error, Description = description, Recommendation = recommendation };
}

/// <summary>
/// Category of health check.
/// </summary>
public enum HealthCategory
{
    /// <summary>
    /// Filesystem accessibility checks (directory exists, files readable, etc.).
    /// </summary>
    Filesystem,

    /// <summary>
    /// Vault integrity checks (vault files exist, decryptable, etc.).
    /// </summary>
    Vault,

    /// <summary>
    /// Credentials configuration checks (credentials present, valid, etc.).
    /// </summary>
    Credentials,

    /// <summary>
    /// Provider health checks (connections active, test status, etc.).
    /// </summary>
    Provider
}

/// <summary>
/// Severity level of a health issue.
/// </summary>
public enum IssueSeverity
{
    /// <summary>
    /// Informational only, no action required.
    /// </summary>
    Info,

    /// <summary>
    /// Minor issue, profile may still be usable.
    /// </summary>
    Warning,

    /// <summary>
    /// Critical issue, profile likely unusable.
    /// </summary>
    Error
}
```

---

## 4. HEALTH CHECKS SPECIFICATION

### 4.1 Filesystem Health (5 passive checks)

**Implementation**: `ProfileHealthChecker.CheckFilesystemHealth(ChromeProfile)`

| # | Check | Detection | Severity | Recommendation |
|---|-------|-----------|----------|----------------|
| 1 | **Profile Directory Exists** | `Directory.Exists(profile.ProfilePath)` | Error | "Profile directory not found. It may have been deleted externally. Consider removing this profile from the catalog." |
| 2 | **Profile Directory Readable** | `Directory.EnumerateFiles(profile.ProfilePath).Any()` with try-catch | Error | "Cannot access profile directory. Check file permissions." |
| 3 | **Local State File Exists** | `File.Exists(Path.Combine(profile.UserDataDirectory, "Local State"))` | Warning | "Chrome Local State file missing. Chrome may not have been launched yet." |
| 4 | **Preferences File Exists** | `File.Exists(Path.Combine(profile.ProfilePath, "Preferences"))` | Warning | "Profile Preferences file missing. Profile may never have been used." |
| 5 | **Secure Preferences File Exists** | `File.Exists(Path.Combine(profile.ProfilePath, "Secure Preferences"))` | Info | "Secure Preferences file missing. This is normal for older Chrome versions or unused profiles." |

**Logic**:
```csharp
public FilesystemHealthResult CheckFilesystemHealth(ChromeProfile profile)
{
    var issues = new List<HealthIssue>();

    // Check #1: Profile directory exists
    if (!Directory.Exists(profile.ProfilePath))
    {
        issues.Add(HealthIssue.Error(
            HealthCategory.Filesystem,
            "Profile directory not found",
            "Profile may have been deleted externally. Consider removing from catalog."));
        // Stop checks if directory doesn't exist
        return new FilesystemHealthResult { Issues = issues };
    }

    // Check #2: Profile directory readable
    try
    {
        Directory.EnumerateFiles(profile.ProfilePath).Any();
    }
    catch (UnauthorizedAccessException)
    {
        issues.Add(HealthIssue.Error(
            HealthCategory.Filesystem,
            "Cannot access profile directory",
            "Check file permissions."));
    }
    catch (IOException ex)
    {
        issues.Add(HealthIssue.Error(
            HealthCategory.Filesystem,
            $"I/O error accessing profile directory: {ex.Message}",
            null));
    }

    // Check #3: Local State file
    var localStatePath = Path.Combine(profile.UserDataDirectory, "Local State");
    if (!File.Exists(localStatePath))
    {
        issues.Add(HealthIssue.Warning(
            HealthCategory.Filesystem,
            "Chrome Local State file missing",
            "Chrome may not have been launched yet."));
    }

    // Check #4: Preferences file
    var preferencesPath = Path.Combine(profile.ProfilePath, "Preferences");
    if (!File.Exists(preferencesPath))
    {
        issues.Add(HealthIssue.Warning(
            HealthCategory.Filesystem,
            "Profile Preferences file missing",
            "Profile may never have been used."));
    }

    // Check #5: Secure Preferences file
    var securePreferencesPath = Path.Combine(profile.ProfilePath, "Secure Preferences");
    if (!File.Exists(securePreferencesPath))
    {
        issues.Add(HealthIssue.Info(
            HealthCategory.Filesystem,
            "Secure Preferences file missing",
            "This is normal for older Chrome versions or unused profiles."));
    }

    return new FilesystemHealthResult { Issues = issues };
}
```

### 4.2 Vault Health (4 passive + 2 active checks)

**Passive Checks** (always on):

| # | Check | Detection | Severity | Recommendation |
|---|-------|-----------|----------|----------------|
| 6 | **Google Vault File Exists** | `File.Exists(vaultPaths.VaultPath)` | Warning | "Google account vault file not found. No Google credentials have been saved yet." |
| 7 | **Provider Vault File Exists** | `File.Exists(providerVaultPath)` | Warning | "Provider connection vault file not found. No provider connections have been saved yet." |
| 8 | **Vault Unlocked** | `credentialsManagerViewModel.IsVaultLocked == false` | Warning | "Vault is locked. Unlock to access credentials." |
| 9 | **Remembered Key File Exists** | `File.Exists(vaultPaths.RememberedKeyPath)` | Info | "Remembered key file missing. Auto-unlock will not work." |

**Active Checks** (on-demand, cached):

| # | Check | Detection | Severity | Recommendation |
|---|-------|-----------|----------|----------------|
| 10 | **Google Vault Decryptable** | Try `TryOpenRememberedAsync()` or test decrypt with null password check | Error | "Google vault corrupted or password changed. Vault may need to be recreated." |
| 11 | **Provider Vault Decryptable** | Check `providerConnectionVaultStore._loadFailed` flag after `EnsureLoadedAsync()` | Error | "Provider vault corrupted. Vault may need to be recreated." |

**Logic**:
```csharp
public VaultHealthResult CheckVaultHealth(
    ChromeProfile profile,
    GoogleAccountVaultPaths vaultPaths,
    GoogleAccountVaultSession? vaultSession,
    bool performActiveChecks)
{
    var issues = new List<HealthIssue>();

    // Passive checks
    if (!File.Exists(vaultPaths.VaultPath))
    {
        issues.Add(HealthIssue.Warning(
            HealthCategory.Vault,
            "Google account vault file not found",
            "No Google credentials have been saved yet."));
    }

    if (vaultSession == null || vaultSession.IsLocked)
    {
        issues.Add(HealthIssue.Warning(
            HealthCategory.Vault,
            "Vault is locked",
            "Unlock to access credentials."));
    }

    if (!File.Exists(vaultPaths.RememberedKeyPath))
    {
        issues.Add(HealthIssue.Info(
            HealthCategory.Vault,
            "Remembered key file missing",
            "Auto-unlock will not work."));
    }

    // Active checks (only if requested)
    if (performActiveChecks)
    {
        // Check #10: Try decrypt Google vault
        try
        {
            var testSession = await _googleAccountVaultStore.TryOpenRememberedAsync(
                vaultPaths.VaultPath,
                CancellationToken.None);
            // Success - vault is decryptable
        }
        catch (CryptographicException)
        {
            issues.Add(HealthIssue.Error(
                HealthCategory.Vault,
                "Google vault corrupted or password changed",
                "Vault may need to be recreated."));
        }

        // Check #11: Provider vault load status
        try
        {
            await _providerConnectionVaultStore.EnsureLoadedAsync();
        }
        catch (CryptographicException)
        {
            issues.Add(HealthIssue.Error(
                HealthCategory.Vault,
                "Provider vault corrupted",
                "Vault may need to be recreated."));
        }
    }

    return new VaultHealthResult { Issues = issues };
}
```

### 4.3 Credentials Configuration (4 passive checks)

| # | Check | Detection | Severity | Recommendation |
|---|-------|-----------|----------|----------------|
| 12 | **Google Credentials Present** | `googleAccountRowViewModel.HasCredentials == true` | Info | "No Google credentials configured for this profile." |
| 13 | **Profile-Credential Resolution** | `ResolveCredentialForProfile(vault, profile) != null` | Warning | "Profile ID not found in vault. Credentials may be linked to a different profile." |
| 14 | **Auto-Login Credentials Available** | `profileProviderStatusViewModel.HasAutoLoginCredentials == true` | Info | "No auto-login credentials available for one or more providers." |
| 15 | **Cross-Vault Consistency** | Profile in `FilteredProfiles` AND has vault record | Warning | "Profile has vault entry but not in catalog, or vice versa." |

**Logic**:
```csharp
public CredentialsHealthResult CheckCredentialsHealth(
    ChromeProfile profile,
    GoogleAccountVault? vault,
    IEnumerable<GoogleAccountRowViewModel> googleAccountRows)
{
    var issues = new List<HealthIssue>();

    // Check #12: Google credentials present
    var row = googleAccountRows.FirstOrDefault(r => r.ProfileId == profile.Id);
    if (row == null || !row.HasCredentials)
    {
        issues.Add(HealthIssue.Info(
            HealthCategory.Credentials,
            "No Google credentials configured for this profile",
            null));
    }

    // Check #13: Profile-credential resolution
    if (vault != null)
    {
        var credential = vault.Find(profile.Id);
        if (credential == null && row?.HasCredentials == true)
        {
            issues.Add(HealthIssue.Warning(
                HealthCategory.Credentials,
                "Profile ID not found in vault",
                "Credentials may be linked to a different profile."));
        }
    }

    // Check #14: Auto-login credentials (checked per provider in provider health)

    // Check #15: Cross-vault consistency
    // (Implementation depends on how FilteredProfiles and vault are compared)

    return new CredentialsHealthResult { Issues = issues };
}
```

### 4.4 Provider Health (3 checks, uses existing infrastructure)

| # | Check | Detection | Severity | Recommendation |
|---|-------|-----------|----------|----------------|
| 16 | **Provider Health State** | `profileProviderStatusViewModel.HealthState != ProviderHealthState.Error` | Error/Warning | "Provider in error state. Check connection settings." |
| 17 | **Connection Test Status** | `providerConnection.HasSuccessfulTestStatus == true` | Warning | "Provider connection test failed or unknown." |
| 18 | **Active Connection Exists** | `providerConnection.IsActive == true` for at least one connection | Warning | "All provider connections are disabled." |

**Logic**:
```csharp
public ProviderHealthResult CheckProviderHealth(
    ChromeProfile profile,
    IEnumerable<ProfileProviderStatusViewModel> providerStatuses)
{
    var issues = new List<HealthIssue>();

    foreach (var status in providerStatuses)
    {
        // Check #16: Provider health state
        if (status.HealthState == ProviderHealthState.Error)
        {
            issues.Add(HealthIssue.Error(
                HealthCategory.Provider,
                $"{status.Definition.DisplayName}: Provider in error state",
                "Check connection settings."));
        }
        else if (status.HealthState == ProviderHealthState.Disabled)
        {
            issues.Add(HealthIssue.Warning(
                HealthCategory.Provider,
                $"{status.Definition.DisplayName}: All connections disabled",
                null));
        }

        // Check #17: Connection test status
        if (!status.IsKnown || status.TestStatus == null)
        {
            issues.Add(HealthIssue.Warning(
                HealthCategory.Provider,
                $"{status.Definition.DisplayName}: Connection test status unknown",
                null));
        }

        // Check #18: Active connection exists
        if (status.ConnectionCount > 0 && !status.IsConnected)
        {
            issues.Add(HealthIssue.Warning(
                HealthCategory.Provider,
                $"{status.Definition.DisplayName}: No active connections",
                "All connections are disabled."));
        }
    }

    return new ProviderHealthResult { Issues = issues };
}
```

---

## 5. CACHING STRATEGY

### Cache Key
```csharp
private record struct CacheKey(string ProfileId);
```

### Cache Entry
```csharp
private sealed class CachedHealthStatus
{
    public ProfileHealthStatus Status { get; init; }
    public DateTime CachedAt { get; init; }
    public TimeSpan TTL { get; init; } = TimeSpan.FromMinutes(5);
    
    public bool IsExpired => DateTime.UtcNow - CachedAt > TTL;
}
```

### Cache Behavior

**Cache Hit** (not expired):
- Return cached `ProfileHealthStatus` immediately
- No checks performed
- Sub-100ms response time

**Cache Miss or Expired**:
- Perform passive checks (always)
- Perform active checks (if explicitly requested)
- Store result in cache with 5-minute TTL
- Return fresh `ProfileHealthStatus`

**Cache Invalidation**:
- On vault unlock/lock
- On profile credentials save/remove
- On manual refresh command
- On profile selection change (optional)

### Implementation

**Location**: `RouterPlus.Infrastructure\Chrome\ProfileHealthService.cs`

```csharp
public sealed class ProfileHealthService
{
    private readonly ProfileHealthChecker _checker;
    private readonly Dictionary<CacheKey, CachedHealthStatus> _cache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public async Task<ProfileHealthStatus> GetHealthStatusAsync(
        ChromeProfile profile,
        bool forceRefresh = false)
    {
        var key = new CacheKey(profile.Id);

        await _cacheLock.WaitAsync();
        try
        {
            // Check cache if not forcing refresh
            if (!forceRefresh && _cache.TryGetValue(key, out var cached) && !cached.IsExpired)
            {
                return cached.Status;
            }

            // Perform health check
            var status = await PerformHealthCheckAsync(profile, performActiveChecks: forceRefresh);

            // Cache result
            _cache[key] = new CachedHealthStatus
            {
                Status = status,
                CachedAt = DateTime.UtcNow
            };

            return status;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public void InvalidateCache(ChromeProfile profile)
    {
        var key = new CacheKey(profile.Id);
        _cacheLock.Wait();
        try
        {
            _cache.Remove(key);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public void InvalidateAllCache()
    {
        _cacheLock.Wait();
        try
        {
            _cache.Clear();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<ProfileHealthStatus> PerformHealthCheckAsync(
        ChromeProfile profile,
        bool performActiveChecks)
    {
        // Aggregate all health check results
        var filesystemResult = _checker.CheckFilesystemHealth(profile);
        var vaultResult = await _checker.CheckVaultHealthAsync(profile, performActiveChecks);
        var credentialsResult = _checker.CheckCredentialsHealth(profile);
        var providerResult = _checker.CheckProviderHealth(profile);

        // Combine all issues
        var allIssues = filesystemResult.Issues
            .Concat(vaultResult.Issues)
            .Concat(credentialsResult.Issues)
            .Concat(providerResult.Issues);

        return ProfileHealthStatus.FromIssues(allIssues);
    }
}
```

---

## 6. UI INTEGRATION

### 6.1 ProfileRowViewModel Changes

**Location**: `RouterPlus.App\ViewModels\ProfileRowViewModel.cs`

```csharp
public sealed class ProfileRowViewModel : INotifyPropertyChanged
{
    private ProfileHealthStatus? _healthStatus;

    // Existing properties...

    /// <summary>
    /// Current health status of this profile.
    /// Null if health check has never been performed.
    /// </summary>
    public ProfileHealthStatus? HealthStatus
    {
        get => _healthStatus;
        set
        {
            if (_healthStatus == value) return;
            _healthStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HealthStatusIcon));
            OnPropertyChanged(nameof(HealthStatusText));
            OnPropertyChanged(nameof(HasHealthIssues));
        }
    }

    /// <summary>
    /// Icon representing health status: "✓" (healthy), "⚠" (warning), "✗" (error), "?" (unknown)
    /// </summary>
    public string HealthStatusIcon => HealthStatus?.Level switch
    {
        HealthLevel.Healthy => "✓",
        HealthLevel.Warning => "⚠",
        HealthLevel.Error => "✗",
        _ => "?"
    };

    /// <summary>
    /// Text summary of health status.
    /// Example: "Profile healthy" / "2 warning(s) detected"
    /// </summary>
    public string HealthStatusText => HealthStatus?.Message ?? "Unknown";

    /// <summary>
    /// Whether this profile has any health issues.
    /// </summary>
    public bool HasHealthIssues => HealthStatus?.Issues.Count > 0;

    // Existing methods...
}
```

### 6.2 MainViewModel Commands

**Location**: `RouterPlus.App\ViewModels\MainViewModel.cs`

```csharp
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ProfileHealthService _profileHealthService;

    // Constructor injection
    public MainViewModel(..., ProfileHealthService profileHealthService)
    {
        // Existing initialization...
        _profileHealthService = profileHealthService;

        // New command
        CheckAllProfilesHealthCommand = new AsyncRelayCommand(
            CheckAllProfilesHealthAsync,
            () => ProfileRows.Any());
    }

    public AsyncRelayCommand CheckAllProfilesHealthCommand { get; }

    private async Task CheckAllProfilesHealthAsync()
    {
        foreach (var row in ProfileRows)
        {
            var status = await _profileHealthService.GetHealthStatusAsync(
                row.Profile,
                forceRefresh: true);
            row.HealthStatus = status;

            // Small delay to avoid overwhelming UI
            await Task.Delay(100);
        }
    }
}
```

### 6.3 Profile List UI Changes

**Location**: `RouterPlus.App\Views\MainWindow.xaml` (profile list section)

Add health status column:

```xml
<DataGridTemplateColumn Header="Health" Width="80">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal" 
                        ToolTip="{Binding HealthStatusText}">
                <TextBlock Text="{Binding HealthStatusIcon}" 
                           FontSize="14"
                           Margin="4,0" />
                <TextBlock Text="{Binding HealthStatusText}" 
                           FontSize="11"
                           Foreground="Gray"
                           VerticalAlignment="Center" />
            </StackPanel>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

Context menu addition:

```xml
<MenuItem Header="Check Profile Health" 
          Command="{Binding DataContext.CheckProfileHealthCommand, RelativeSource={RelativeSource AncestorType=Window}}"
          CommandParameter="{Binding}" />
```

### 6.4 Health Issues Popover

**New Component**: `RouterPlus.App\Views\ProfileHealthPopover.xaml`

Shows detailed issues list when user clicks health status:

```xml
<Popup x:Name="HealthIssuesPopup" StaysOpen="False">
    <Border Background="White" BorderBrush="Gray" BorderThickness="1" 
            Padding="8" MaxWidth="400">
        <StackPanel>
            <TextBlock Text="Profile Health Issues" 
                       FontWeight="Bold" 
                       Margin="0,0,0,8"/>
            
            <ItemsControl ItemsSource="{Binding HealthStatus.Issues}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Margin="0,4" 
                                Padding="8" 
                                Background="{Binding Severity, Converter={StaticResource SeverityToColorConverter}}">
                            <StackPanel>
                                <TextBlock Text="{Binding Description}" 
                                           FontWeight="SemiBold"/>
                                <TextBlock Text="{Binding Recommendation}" 
                                           Foreground="Gray"
                                           TextWrapping="Wrap"
                                           Margin="0,4,0,0"
                                           Visibility="{Binding Recommendation, Converter={StaticResource NullToVisibilityConverter}}"/>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <TextBlock Text="Last checked:" 
                       Foreground="Gray" 
                       Margin="0,8,0,0"/>
            <TextBlock Text="{Binding HealthStatus.LastChecked, StringFormat='{}{0:yyyy-MM-dd HH:mm:ss}'}" 
                       Foreground="Gray"/>
        </StackPanel>
    </Border>
</Popup>
```

---

## 7. IMPLEMENTATION PHASES

### Phase 1: Core Models & Filesystem Checks (Day 1)

**Tasks**:
1. Create `ProfileHealthStatus.cs` model
2. Create `HealthIssue.cs` model
3. Create `ProfileHealthChecker.cs` with filesystem checks only
4. Unit tests for models and filesystem checks

**Deliverables**:
- 3 new files in `RouterPlus.Core\Chrome\`
- 15+ unit tests covering all enum values, factory methods, filesystem checks

### Phase 2: Vault & Credentials Checks (Day 2)

**Tasks**:
1. Implement vault health checks (passive + active)
2. Implement credentials configuration checks
3. Unit tests for vault/credentials checks
4. Integration tests with test vaults

**Deliverables**:
- Vault checking logic in `ProfileHealthChecker`
- 20+ unit tests covering vault scenarios (locked, missing, corrupted)

### Phase 3: Infrastructure & Caching (Day 2-3)

**Tasks**:
1. Create `ProfileHealthService.cs` with caching
2. Wire up dependency injection
3. Cache invalidation hooks (vault unlock, credentials save)
4. Unit tests for caching behavior

**Deliverables**:
- `ProfileHealthService` in `RouterPlus.Infrastructure\Chrome\`
- 10+ tests for cache hit/miss/invalidation

### Phase 4: UI Integration (Day 3)

**Tasks**:
1. Add `HealthStatus` property to `ProfileRowViewModel`
2. Add `CheckAllProfilesHealthCommand` to `MainViewModel`
3. Update profile list XAML with health column
4. Add context menu item
5. Create severity-to-color converter

**Deliverables**:
- Modified `ProfileRowViewModel.cs`, `MainViewModel.cs`
- Updated `MainWindow.xaml`
- `SeverityToColorConverter.cs`

### Phase 5: Health Issues Popover (Day 4)

**Tasks**:
1. Create `ProfileHealthPopover.xaml` component
2. Wire up click handler to show popover
3. Style issues list (colors per severity)
4. Test with various issue combinations

**Deliverables**:
- `ProfileHealthPopover.xaml` and `.xaml.cs`
- Click-to-show interaction

### Phase 6: Testing & Polish (Day 4)

**Tasks**:
1. End-to-end testing of all health checks
2. Performance testing (cache effectiveness)
3. UI/UX polish (icons, spacing, colors)
4. Documentation updates

**Deliverables**:
- E2E test suite
- Performance benchmark results
- Updated user documentation

---

## 8. TESTING STRATEGY

### 8.1 Unit Tests

**Location**: `tests\RouterPlus.Core.Tests\Chrome\ProfileHealthCheckerTests.cs`

**Coverage**:
- `ProfileHealthStatus.FromIssues()` with 0, 1, multiple issues
- `HealthLevel` computation (Error > Warning > Healthy)
- Each filesystem check in isolation
- Each vault check in isolation
- Exception handling (IOException, UnauthorizedAccessException, CryptographicException)

**Example Test**:
```csharp
[Fact]
public void CheckFilesystemHealth_ProfileDirectoryMissing_ReturnsError()
{
    // Arrange
    var profile = new ChromeProfile(
        "test-id",
        "Test Profile",
        "NonExistentDirectory",
        "C:\\Temp\\UserData",
        false);
    var checker = new ProfileHealthChecker();

    // Act
    var result = checker.CheckFilesystemHealth(profile);

    // Assert
    Assert.Single(result.Issues);
    var issue = result.Issues[0];
    Assert.Equal(HealthCategory.Filesystem, issue.Category);
    Assert.Equal(IssueSeverity.Error, issue.Severity);
    Assert.Contains("directory not found", issue.Description);
}
```

### 8.2 Integration Tests

**Location**: `tests\RouterPlus.Infrastructure.Tests\Chrome\ProfileHealthServiceTests.cs`

**Coverage**:
- Cache hit returns same instance
- Cache miss performs checks
- Cache expiration after TTL
- Cache invalidation on vault unlock
- Batch check sequencing
- Thread safety (concurrent cache access)

**Example Test**:
```csharp
[Fact]
public async Task GetHealthStatusAsync_CacheHit_ReturnsImmediately()
{
    // Arrange
    var service = new ProfileHealthService(mockChecker, mockVault, mockCredentials);
    var profile = CreateTestProfile();

    // First call - cache miss
    var status1 = await service.GetHealthStatusAsync(profile);
    var stopwatch = Stopwatch.StartNew();

    // Act - Second call - cache hit
    var status2 = await service.GetHealthStatusAsync(profile);
    stopwatch.Stop();

    // Assert
    Assert.Same(status1, status2); // Same instance
    Assert.True(stopwatch.ElapsedMilliseconds < 10); // Sub-10ms
}
```

### 8.3 End-to-End Tests

**Location**: `tests\RouterPlus.App.E2E\ProfileHealthE2ETests.cs`

**Scenarios**:
1. Healthy profile: All checks pass, green icon shown
2. Warning profile: Preferences missing, yellow icon + tooltip
3. Error profile: Directory deleted, red icon + detailed popover
4. Batch check: 10 profiles checked sequentially, results displayed
5. Cache refresh: Manual "Check Health" invalidates cache, fresh check performed

---

## 9. PERFORMANCE TARGETS

### Response Time

| Operation | Target | Maximum |
|-----------|--------|---------|
| Passive checks (cached) | <10ms | <50ms |
| Passive checks (uncached) | <100ms | <200ms |
| Active checks (uncached) | <500ms | <1000ms |
| Batch check (10 profiles) | <2s | <5s |

### Memory Impact

| Component | Footprint |
|-----------|-----------|
| `ProfileHealthStatus` per profile | ~500 bytes |
| Cache for 100 profiles | ~50 KB |
| UI bindings | Negligible |

### Cache Effectiveness

**Target**: 90% cache hit rate during normal usage

**Measurement**:
- Track cache hits/misses in `ProfileHealthService`
- Log ratio every 100 checks
- Alert if hit rate drops below 80%

---

## 10. RISKS & MITIGATIONS

### Risk 1: Active Checks Too Slow
**Impact**: User waits >1s for health check result  
**Likelihood**: Medium  
**Mitigation**:
- Cache active check results for 5 minutes
- Only perform active checks on explicit "Refresh" command
- Show "Checking..." spinner during active checks

### Risk 2: Vault Decryption Fails on Active Check
**Impact**: False positive "vault corrupted" error  
**Likelihood**: Low  
**Mitigation**:
- Catch `CryptographicException` separately from other exceptions
- Distinguish "wrong password" from "corrupted data"
- Provide clear error message: "Vault requires password" vs "Vault corrupted"

### Risk 3: Too Many False Warnings
**Impact**: Users ignore health warnings (alarm fatigue)  
**Likelihood**: Medium  
**Mitigation**:
- Carefully tune severity levels (use Info for non-issues)
- Only show Warning/Error in main UI
- Hide Info-level issues in collapsed section

### Risk 4: Cache Staleness
**Impact**: Health status shown is outdated  
**Likelihood**: Low  
**Mitigation**:
- 5-minute TTL is short enough for most scenarios
- Manual refresh always bypasses cache
- Auto-invalidate on vault operations

### Risk 5: Performance Regression on Large Profile Lists
**Impact**: UI freezes during batch check  
**Likelihood**: Low  
**Mitigation**:
- 100ms delay between batch checks
- Run batch check on background thread
- Show progress indicator during batch operation

---

## 11. FUTURE ENHANCEMENTS

### Post-v1 Improvements

1. **Automated Remediation**
   - Auto-remove orphaned vault entries
   - Auto-cleanup temp files
   - One-click "Fix Issues" button

2. **Health History Tracking**
   - Store health check results over time
   - Trend analysis (profile degrading over time)
   - Alert on sudden state changes

3. **Health Dashboard**
   - Aggregated view of all profiles
   - Filter by health level (show only errors)
   - Export health report to CSV

4. **Background Monitoring**
   - Periodic health checks (every 30 minutes)
   - Toast notification on new errors
   - Status bar indicator: "2 profiles need attention"

5. **Provider-Specific Checks**
   - Codex connection validation
   - API key expiration warnings
   - Rate limit status

---

## 12. OPEN QUESTIONS

1. **Should we check Chrome executable version compatibility?**
   - Agent report mentioned this but didn't prioritize it
   - Could warn if profile created by newer Chrome than installed
   - **Decision**: Defer to v2 (low priority)

2. **Should we validate Local State JSON structure?**
   - Active check could parse Local State and detect corruption
   - Adds complexity + performance cost
   - **Decision**: Yes, add as active check (Check #19)

3. **Should we track health check history?**
   - Useful for debugging intermittent issues
   - Adds persistence complexity
   - **Decision**: Defer to v2

4. **Should we auto-refresh health status periodically?**
   - Pro: Always current
   - Con: Background CPU/disk activity
   - **Decision**: No auto-refresh in v1, manual only

---

## 13. SUMMARY

### What We're Building

A **Smart Passive Monitoring** system that:
- Performs 13 passive checks (zero cost, always on)
- Performs 6 active checks (on-demand, cached 5 minutes)
- Displays health status icon + text in profile list
- Shows detailed issues in popover with recommendations
- Supports batch health checking

### Why This Approach

- **Balanced**: Comprehensive enough to catch real issues, performant enough for large profile lists
- **Actionable**: Every issue includes a recommendation
- **Non-intrusive**: Integrates into existing UI, no new tabs/views
- **Extensible**: Easy to add more checks later

### Implementation Timeline

**4 days** total:
- Day 1: Core models + filesystem checks
- Day 2: Vault/credentials checks + infrastructure
- Day 3: UI integration
- Day 4: Popover + testing/polish

### Success Criteria

- ✅ Users can see health status in <2 seconds
- ✅ Passive checks complete in <100ms
- ✅ Active checks cached for 5 minutes
- ✅ 90% of common issues detected and surfaced
- ✅ All unit/integration tests pass
- ✅ E2E scenarios validated

---

**Document Version**: 1.0  
**Last Updated**: 2026-09-03  
**Status**: Ready for Implementation  
**Next Step**: Invoke `writing-plans` skill to create detailed implementation plan