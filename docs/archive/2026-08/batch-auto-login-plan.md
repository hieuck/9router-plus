# Batch Auto Login Feature Plan

**Created:** 2026-08-28  
**User Request:** "viết kế hoạch chi tiết vào docs trong repo để tránh quên"  
**Status:** Planning  
**Estimate:** 7-11 hours implementation

---

## Context

This planning document extends the existing Google Auto Login feature to support batch operations.

**Affected Components:**
- `MainViewModel` - Add multi-select mode, batch orchestration
- `ProfileRowViewModel` - Add selection state and vault indicator
- `GoogleLoginCdpBrowser` - Reuse existing automation
- `GoogleLoginVaultStore` - Check credential availability
- `ChromeLauncher` - Launch browser per profile

**New Data Models:**
- `BatchLoginProgressRow` - Tracks per-profile progress (profile, state, message, duration)
- `BatchLoginState` enum - Waiting | InProgress | Success | Failed | Skipped

**UI Changes:**
- Multi-select mode toggle button in toolbar
- Checkboxes in profile list
- Bulk actions bar
- Progress overlay panel

---

## Overview

Extend the existing Google Auto Login feature (currently accessible via context menu) to support:
1. **Multi-select mode** - Select multiple Chrome profiles
2. **Batch sequential login** - Auto-login profiles one by one
3. **Auto-skip** - Skip profiles without vault credentials
4. **Continue on failure** - Don't stop batch when one profile fails

---

## User Flow

### Single Profile (Existing)
```
Right-click profile → "Tự động đăng nhập Google" → Dialog opens
```

### Batch Login (New)
```
1. Click "☑ Chọn nhiều" button (toolbar)
2. Checkboxes appear in profile list
3. Select multiple profiles (🔐 icon shows which have vault creds)
4. Click "🔐 Auto Login All" in bulk actions bar
5. Progress panel opens showing live status
6. Profiles login sequentially with 2s delay between each
7. Summary shown when complete
```

---

## UI Components

### 1. Multi-Select Toggle Button
**Location:** Toolbar (Grid.Row="0", Grid.Column="1")  
**Position:** After "↻ Đồng bộ" button

```xaml
<Button Content="☑ Chọn nhiều" 
        Command="{Binding ToggleMultiSelectModeCommand}"
        ToolTip="Bật/tắt chế độ chọn nhiều profiles">
    <Button.Style>
        <!-- Highlight when IsMultiSelectMode = true -->
        <DataTrigger Binding="{Binding IsMultiSelectMode}" Value="True">
            <Setter Property="Background" Value="{DynamicResource AccentSoftBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
        </DataTrigger>
    </Button.Style>
</Button>
```

**Keyboard shortcut:** Consider adding Ctrl+M or similar

---

### 2. Profile List Checkboxes
**Location:** `ProfileListItemStyle` in MainWindow.xaml (line ~798)

**Grid columns update:**
```xaml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="Auto" /> <!-- NEW: Checkbox (28px) -->
    <ColumnDefinition Width="24" />   <!-- Index -->
    <ColumnDefinition Width="36" />   <!-- Avatar -->
    <ColumnDefinition Width="*" />    <!-- Profile info -->
    <ColumnDefinition Width="Auto" /> <!-- NEW: Vault indicator 🔐 -->
    <ColumnDefinition Width="Auto" /> <!-- Arrow › -->
</Grid.ColumnDefinitions>
```

**Checkbox:**
- Only visible when `IsMultiSelectMode = true`
- Bound to `ProfileRowViewModel.IsSelected`
- Allow Ctrl+Click on profile to toggle selection

**Vault indicator (🔐 icon):**
- Shows when profile has valid vault credentials
- Always visible (not just in multi-select mode)
- ToolTip: "Có thông tin đăng nhập trong vault"

---

### 3. Bulk Actions Bar
**Location:** Between toolbar and content area  
**Visibility:** Only when `HasSelectedProfiles = true`

```xaml
<Border Background="{DynamicResource AccentSoftBrush}"
        BorderBrush="{DynamicResource AccentBrush}"
        BorderThickness="0,0,0,1"
        Padding="26,12">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        
        <!-- Left: Selection summary -->
        <StackPanel Orientation="Horizontal">
            <TextBlock Text="✓" FontSize="16" Margin="0,0,8,0" />
            <TextBlock Text="{Binding SelectedProfilesText}" 
                       FontSize="12" FontWeight="SemiBold" />
            <!-- e.g. "5 profiles selected" -->
            
            <TextBlock Text=" · " Margin="8,0" />
            
            <TextBlock Text="{Binding SelectedProfilesWithVaultText}" 
                       FontSize="11" 
                       Foreground="{DynamicResource MutedTextBrush}" />
            <!-- e.g. "3 with vault credentials" -->
        </StackPanel>
        
        <!-- Right: Actions -->
        <StackPanel Grid.Column="1" Orientation="Horizontal">
            <Button Content="🔐 Auto Login All" 
                    Command="{Binding StartBatchAutoLoginCommand}"
                    IsEnabled="{Binding HasProfilesWithVault}"
                    Style="{StaticResource ProviderPrimaryActionButtonStyle}" />
            
            <Button Content="Cancel Selection" 
                    Command="{Binding ClearSelectionCommand}" />
        </StackPanel>
    </Grid>
</Border>
```

**Quick actions to consider:**
- "Select profiles with vault credentials"
- "Select all visible profiles"

---

### 4. Batch Progress Panel
**Location:** Overlay on Grid.Row="1" Grid.Column="1"  
**Trigger:** Opens when batch login starts

```xaml
<!-- Dark overlay -->
<Border Background="#99000000"
        Visibility="{Binding IsBatchLoginRunning, ...}">
    
    <!-- Centered modal panel -->
    <Border Width="600" Height="400" 
            Background="{DynamicResource SurfaceBrush}"
            CornerRadius="16"
            Padding="24">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />  <!-- Header -->
                <RowDefinition Height="*" />     <!-- Progress list -->
                <RowDefinition Height="Auto" />  <!-- Actions -->
            </Grid.RowDefinitions>
            
            <!-- Header -->
            <StackPanel>
                <TextBlock Text="Batch Auto Login Progress" 
                           FontSize="16" FontWeight="SemiBold" />
                <TextBlock Text="{Binding BatchProgressSummary}" 
                           FontSize="11" 
                           Foreground="{DynamicResource MutedTextBrush}" />
                <!-- e.g. "3/5 completed · 2 success · 1 failed" -->
            </StackPanel>
            
            <!-- Progress List (scrollable) -->
            <ScrollViewer Grid.Row="1" Margin="0,16">
                <ItemsControl ItemsSource="{Binding BatchProgressRows}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Padding="0,12" 
                                    BorderThickness="0,0,0,1"
                                    BorderBrush="{DynamicResource BorderBrush}">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto" /> <!-- Icon -->
                                        <ColumnDefinition Width="*" />    <!-- Info -->
                                        <ColumnDefinition Width="Auto" /> <!-- Duration -->
                                    </Grid.ColumnDefinitions>
                                    
                                    <!-- Status Icon -->
                                    <TextBlock Text="{Binding StatusIcon}" 
                                               FontSize="16" />
                                    <!-- ⏸ Waiting | ⏳ InProgress | ✅ Success | ❌ Failed | ⊘ Skipped -->
                                    
                                    <!-- Profile Info -->
                                    <StackPanel Grid.Column="1" Margin="12,0">
                                        <TextBlock Text="{Binding ProfileName}" 
                                                   FontSize="12" FontWeight="SemiBold" />
                                        <TextBlock Text="{Binding StatusMessage}" 
                                                   FontSize="10"
                                                   Foreground="{DynamicResource MutedTextBrush}" />
                                    </StackPanel>
                                    
                                    <!-- Duration -->
                                    <TextBlock Grid.Column="2" 
                                               Text="{Binding DurationText}" 
                                               FontSize="10" />
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
            
            <!-- Actions -->
            <StackPanel Grid.Row="2" 
                        Orientation="Horizontal" 
                        HorizontalAlignment="Right">
                <Button Content="Stop All" 
                        Command="{Binding StopBatchLoginCommand}"
                        Visibility="{Binding CanStopBatch, ...}" />
                
                <Button Content="Close" 
                        Command="{Binding CloseBatchProgressCommand}"
                        Visibility="{Binding CanCloseBatch, ...}" />
            </StackPanel>
        </Grid>
    </Border>
</Border>
```

---

## Data Models

### ProfileRowViewModel (extend existing)
```csharp
public class ProfileRowViewModel : ViewModelBase
{
    // Existing properties...
    
    // NEW: Multi-select support
    public bool IsSelected { get; set; }
    
    // NEW: Vault credentials indicator
    public bool HasVaultCredentials { get; set; }
    public string VaultIndicatorVisibility => 
        HasVaultCredentials ? "Visible" : "Collapsed";
}
```

### BatchLoginProgressRow (new model)
```csharp
public class BatchLoginProgressRow : ViewModelBase
{
    public ChromeProfile Profile { get; }
    public string ProfileName => Profile.Name;
    
    public BatchLoginState State { get; set; }
    
    public string StatusIcon => State switch
    {
        BatchLoginState.Waiting => "⏸",
        BatchLoginState.InProgress => "⏳",
        BatchLoginState.Success => "✅",
        BatchLoginState.Failed => "❌",
        BatchLoginState.Skipped => "⊘",
        _ => "?"
    };
    
    public string StatusMessage { get; set; }
    public TimeSpan Duration { get; set; }
    public string DurationText => Duration.TotalSeconds > 0 
        ? $"{Duration.TotalSeconds:F1}s" 
        : "";
}

public enum BatchLoginState
{
    Waiting,
    InProgress,
    Success,
    Failed,
    Skipped
}
```

---

## MainViewModel Changes

### New Properties
```csharp
// Multi-select mode
public bool IsMultiSelectMode { get; set; }

// Selected profiles
public ObservableCollection<ChromeProfile> SelectedProfiles { get; }

// Computed properties
public string SelectedProfilesText => 
    $"{SelectedProfiles.Count} profiles selected";

public string SelectedProfilesWithVaultText
{
    get
    {
        var withVault = SelectedProfiles.Count(p => HasVaultCredentials(p));
        return $"{withVault} with vault credentials";
    }
}

public bool HasSelectedProfiles => SelectedProfiles.Count > 0;

public bool HasProfilesWithVault => 
    SelectedProfiles.Any(p => HasVaultCredentials(p));

// Batch progress
public bool IsBatchLoginRunning { get; set; }
public ObservableCollection<BatchLoginProgressRow> BatchProgressRows { get; }

public string BatchProgressSummary
{
    get
    {
        var completed = BatchProgressRows.Count(r => 
            r.State == BatchLoginState.Success || 
            r.State == BatchLoginState.Failed ||
            r.State == BatchLoginState.Skipped);
        var success = BatchProgressRows.Count(r => 
            r.State == BatchLoginState.Success);
        var failed = BatchProgressRows.Count(r => 
            r.State == BatchLoginState.Failed);
        
        return $"{completed}/{BatchProgressRows.Count} completed · " +
               $"{success} success · {failed} failed";
    }
}

public bool CanStopBatch => IsBatchLoginRunning;
public bool CanCloseBatch => !IsBatchLoginRunning;
```

### New Commands
```csharp
public ICommand ToggleMultiSelectModeCommand { get; }
public ICommand SelectAllProfilesCommand { get; }
public ICommand ClearSelectionCommand { get; }
public ICommand StartBatchAutoLoginCommand { get; }
public ICommand StopBatchLoginCommand { get; }
public ICommand CloseBatchProgressCommand { get; }
```

### Core Logic: Batch Auto Login
```csharp
private CancellationTokenSource? _batchLoginCts;

private async Task StartBatchAutoLoginAsync()
{
    if (SelectedProfiles.Count == 0) return;
    
    // Setup
    IsBatchLoginRunning = true;
    BatchProgressRows.Clear();
    _batchLoginCts = new CancellationTokenSource();
    
    try
    {
        await RunBatchAutoLoginAsync(_batchLoginCts.Token);
        
        // Show summary notification
        var summary = BatchProgressSummary;
        _toastService.ShowSuccess($"Batch login complete: {summary}");
    }
    catch (OperationCanceledException)
    {
        _toastService.ShowInfo("Batch login stopped by user");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Batch auto login failed");
        _toastService.ShowError($"Batch login error: {ex.Message}");
    }
    finally
    {
        IsBatchLoginRunning = false;
        _batchLoginCts?.Dispose();
        _batchLoginCts = null;
    }
}

private async Task RunBatchAutoLoginAsync(CancellationToken ct)
{
    foreach (var profile in SelectedProfiles)
    {
        if (ct.IsCancellationRequested) break;
        
        var progressRow = new BatchLoginProgressRow(profile)
        {
            State = BatchLoginState.InProgress,
            StatusMessage = "Checking vault credentials..."
        };
        BatchProgressRows.Add(progressRow);
        
        var sw = Stopwatch.StartNew();
        
        try
        {
            // Check vault credentials
            var hasCredentials = await HasVaultCredentialsAsync(profile, ct);
            if (!hasCredentials)
            {
                progressRow.State = BatchLoginState.Skipped;
                progressRow.StatusMessage = "No vault credentials";
                progressRow.Duration = sw.Elapsed;
                _logger.LogInformation(
                    "Skipped {Profile} - no vault credentials", 
                    profile.Name);
                continue; // Auto-skip and continue
            }
            
            // Run auto login for this profile
            progressRow.StatusMessage = "Launching browser...";
            await RunSingleAutoLoginAsync(profile, progressRow, ct);
            
            progressRow.State = BatchLoginState.Success;
            progressRow.StatusMessage = "Login successful";
            _logger.LogInformation(
                "Auto login succeeded for {Profile} in {Duration:F1}s", 
                profile.Name, 
                sw.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            progressRow.State = BatchLoginState.Failed;
            progressRow.StatusMessage = "Cancelled";
            throw; // Propagate cancellation
        }
        catch (Exception ex)
        {
            progressRow.State = BatchLoginState.Failed;
            progressRow.StatusMessage = ex.Message;
            _logger.LogError(
                ex, 
                "Auto login failed for {Profile}", 
                profile.Name);
            // Continue with next profile (don't throw)
        }
        finally
        {
            progressRow.Duration = sw.Elapsed;
        }
        
        // Delay between profiles to avoid rate limiting
        if (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
        }
    }
}

private async Task<bool> HasVaultCredentialsAsync(
    ChromeProfile profile, 
    CancellationToken ct)
{
    // Check if vault has credentials for this profile's email
    var email = profile.Name; // Or get from profile metadata
    
    try
    {
        var vaultPath = _googleLoginVaultPaths.VaultPath;
        if (!File.Exists(vaultPath)) return false;
        
        var vault = await _googleLoginVaultStore.LoadVaultAsync(
            vaultPath, 
            ct);
        
        return vault.Credentials.ContainsKey(email);
    }
    catch
    {
        return false;
    }
}

private async Task RunSingleAutoLoginAsync(
    ChromeProfile profile,
    BatchLoginProgressRow progressRow,
    CancellationToken ct)
{
    // Launch Chrome with profile
    progressRow.StatusMessage = "Launching Chrome...";
    var chromeSession = await _chromeLauncher.LaunchManagedAsync(
        _installation,
        profile,
        new Uri("https://accounts.google.com/"),
        ct,
        useOriginalProfile: true);
    
    try
    {
        // Get credentials from vault
        progressRow.StatusMessage = "Loading credentials from vault...";
        var vault = await _googleLoginVaultStore.LoadVaultAsync(
            _googleLoginVaultPaths.VaultPath,
            ct);
        
        var email = profile.Name;
        if (!vault.Credentials.TryGetValue(email, out var creds))
        {
            throw new InvalidOperationException("Credentials not found in vault");
        }
        
        // Create TOTP generator if available
        ITotpGenerator? totpGenerator = null;
        if (!string.IsNullOrEmpty(creds.TotpSecret))
        {
            totpGenerator = new GoogleTotpGenerator(creds.TotpSecret);
        }
        
        // Run automation
        progressRow.StatusMessage = "Automating login...";
        using var automation = new GoogleLoginCdpBrowser(
            chromeSession.WebSocketUrl!,
            email,
            creds.Password,
            totpGenerator);
        
        await automation.LoginAsync(ct);
        
        progressRow.StatusMessage = "Login automation complete";
    }
    finally
    {
        // Cleanup
        progressRow.StatusMessage = "Closing browser...";
        await chromeSession.CleanupAsync();
    }
}

private void StopBatchLogin()
{
    _batchLoginCts?.Cancel();
}
```

---

## Implementation Phases

### Phase 1: Multi-Select UI (1-2 hours)
**Goal:** User can select multiple profiles with checkboxes

**Tasks:**
1. Add `IsMultiSelectMode` property to `MainViewModel`
2. Add toggle button to toolbar
3. Add `IsSelected` to `ProfileRowViewModel`
4. Add checkbox column to profile list (visible only when multi-select mode)
5. Add vault indicator (🔐) column
6. Add bulk actions bar UI
7. Implement `ToggleMultiSelectModeCommand`
8. Implement `ClearSelectionCommand`

**Files:**
- `MainWindow.xaml` - UI changes
- `ViewModels/MainViewModel.cs` - properties + commands
- `ViewModels/ProfileRowViewModel.cs` - IsSelected property

**Test:**
- Toggle multi-select mode shows/hides checkboxes
- Clicking checkboxes updates selection count
- Bulk actions bar appears when profiles selected
- Vault indicator shows on profiles with credentials

---

### Phase 2: Vault Credentials Check (1 hour)
**Goal:** Identify which profiles have vault credentials

**Tasks:**
1. Add `HasVaultCredentials` property to `ProfileRowViewModel`
2. Implement `HasVaultCredentialsAsync()` helper
3. Update profile rows with vault status on load
4. Update vault status after vault operations

**Files:**
- `ViewModels/ProfileRowViewModel.cs`
- `ViewModels/MainViewModel.cs`

**Test:**
- 🔐 icon appears on profiles with vault creds
- Count in bulk actions bar is accurate

---

### Phase 3: Batch Progress UI (2 hours)
**Goal:** Display live progress during batch login

**Tasks:**
1. Create `BatchLoginProgressRow` model
2. Add batch progress overlay to `MainWindow.xaml`
3. Add progress properties to `MainViewModel`
4. Implement `CloseBatchProgressCommand`
5. Wire up status icon mapping

**New files:**
- `Models/BatchLoginProgressRow.cs`

**Files modified:**
- `MainWindow.xaml`
- `ViewModels/MainViewModel.cs`

**Test:**
- Progress panel appears when batch starts
- Progress rows update in real-time
- Summary text is accurate
- Close button works after completion

---

### Phase 4: Batch Login Logic (3-4 hours)
**Goal:** Sequential auto-login with skip and continue-on-failure

**Tasks:**
1. Extract single auto-login logic to `RunSingleAutoLoginAsync()`
2. Implement `RunBatchAutoLoginAsync()` sequential runner
3. Add vault credential check with auto-skip
4. Add error handling with continue-on-failure
5. Add 2s delay between profiles
6. Implement `StartBatchAutoLoginCommand`
7. Implement `StopBatchLoginCommand` with CancellationToken
8. Add logging for each profile
9. Add completion summary notification

**Files:**
- `ViewModels/MainViewModel.cs` - core logic

**Consider extracting to:**
- `Services/BatchAutoLoginService.cs` (if logic gets complex)

**Test:**
- Batch runs profiles sequentially
- Profiles without vault creds are skipped
- Failures don't stop the batch
- Cancellation works immediately
- 2s delay between profiles

---

### Phase 5: Polish & UX (1-2 hours)
**Goal:** Smooth user experience

**Tasks:**
1. Add keyboard shortcuts:
   - `Ctrl+A` - Select all visible profiles
   - `Ctrl+Shift+A` - Select profiles with vault creds
   - `Escape` - Exit multi-select mode / close progress panel
2. Add quick action: "Select profiles with vault credentials"
3. Improve progress messages (more detailed states)
4. Add sound/notification when batch completes (optional)
5. Auto-exit multi-select mode after batch completes
6. Persist multi-select mode preference? (optional)
7. Add tooltip showing what will happen on "Auto Login All"

**Files:**
- `MainWindow.xaml` - input bindings
- `ViewModels/MainViewModel.cs` - commands

**Test:**
- Keyboard shortcuts work correctly
- UX feels smooth and responsive
- Users understand what's happening

---

## Error Handling

### Scenarios to handle:
1. **Profile without vault credentials** → Skip with status message
2. **Chrome launch fails** → Mark as failed, continue batch
3. **Google automation fails** (wrong password, TOTP issue) → Mark as failed, continue
4. **User cancels batch** → Stop gracefully, show partial results
5. **Chrome already running for profile** → Show error or auto-close existing?

### Logging:
```csharp
// At batch start
_logger.LogInformation(
    "Starting batch auto login for {Count} profiles", 
    SelectedProfiles.Count);

// Per profile
_logger.LogInformation(
    "Auto login {State} for {Profile} in {Duration:F1}s: {Message}",
    state, profileName, duration, statusMessage);

// At batch end
_logger.LogInformation(
    "Batch auto login complete: {Summary}", 
    BatchProgressSummary);
```

---

## Testing Checklist

### Unit Tests
- [ ] `HasVaultCredentialsAsync()` returns correct result
- [ ] Progress row state transitions correctly
- [ ] Summary text computed correctly
- [ ] Selection commands work (select all, clear)

### Integration Tests
- [ ] Single profile auto-login still works via context menu
- [ ] Batch login runs sequentially
- [ ] Profiles without creds are skipped
- [ ] Failures don't stop batch
- [ ] Cancellation works mid-batch

### Manual Testing
- [ ] UI responds correctly in multi-select mode
- [ ] Checkboxes toggle selection
- [ ] Vault indicator shows correctly
- [ ] Bulk actions bar appears/disappears
- [ ] Progress panel shows live updates
- [ ] Stop button works immediately
- [ ] Close button works after completion
- [ ] Summary notification is accurate
- [ ] Keyboard shortcuts work
- [ ] Works with 1, 5, 20+ profiles

### Edge Cases
- [ ] All profiles have vault creds
- [ ] No profiles have vault creds
- [ ] Mixed (some with, some without)
- [ ] All profiles fail
- [ ] All profiles succeed
- [ ] Cancel immediately after starting
- [ ] Cancel in middle of batch
- [ ] Chrome already running for selected profile

---

## Future Enhancements (Not in scope)

1. **Parallel login** - Run N profiles in parallel (configurable)
2. **Retry logic** - Auto-retry failed profiles with backoff
3. **Scheduled batch login** - Run batch at specific times
4. **Profile groups** - Save selection as named groups
5. **Batch login templates** - Pre-configured batches
6. **Progress export** - Export batch results to CSV
7. **Email notification** - Notify when long batch completes

---

## Related Code

### Existing Auto Login Components
- `GoogleLoginCdpBrowser.cs` - Single login automation
- `GoogleLoginVaultStore.cs` - Vault storage
- `GoogleAutoLoginDialog.xaml` - Single profile UI (context menu)
- `ChromeLauncher.cs` - Browser launching

### Files to Modify
- `MainWindow.xaml` - UI changes
- `ViewModels/MainViewModel.cs` - Properties, commands, logic
- `ViewModels/ProfileRowViewModel.cs` - Selection state

### Files to Create
- `Models/BatchLoginProgressRow.cs` - Progress tracking model
- (Optional) `Services/BatchAutoLoginService.cs` - Extract batch logic

---

## References

- Original discussion: Context from 2026-08-28 session
- Related features: Google Auto Login (context menu), Codex/Kiro OAuth automation
- Future refactoring: AutoLogin code overlap (Codex/Kiro/Google)
