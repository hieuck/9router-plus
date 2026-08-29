# Batch Auto Login Feature Plan (v2 - Updated for Refactored Architecture)

**Created:** 2026-08-28  
**Updated:** 2026-08-29 (Post Auto-Login Vault Refactor)  
**Status:** Ready to Implement  
**Estimate:** 6-9 hours implementation

---

## Context

This plan implements batch sequential auto-login using the **refactored architecture** from Phases 1-6.

**Current Architecture (Post-Refactor):**
- `AutoLoginOrchestrator` - Unified orchestrator with fallback support
- `ProviderConnectionVaultStore` - Maps profile → provider → auth config
- `GoogleAccountVaultStore` - Google credentials only
- Multi-provider support: Codex, Kiro, GitHub, OpenRouter
- Dual auth methods: Google OAuth + Direct login per provider

**Key API:**
- `AutoLoginOrchestrator.LoginAsync(profileName, provider, startUri, timeout, ct)` → `AutoLoginResult`
- `ProviderConnectionVaultStore.HasCredentialsAsync(profileName, provider, ct)` → `bool`
- `AutoLoginResult`: `{ Success, Method, ErrorMessage }`

---

## Overview

Extend auto-login to support:
1. **Multi-select mode** - Select multiple Chrome profiles
2. **Multi-provider batch** - Each profile can login to multiple providers
3. **Sequential execution** - One profile-provider pair at a time
4. **Auto-skip** - Skip profile-provider pairs without credentials
5. **Continue on failure** - Don't stop batch when one login fails

---

## User Flow

### Current (Single Profile, Single Provider)
```
Click provider dot → Auto-login dialog → Select auth method → Login
```

### New (Batch)
```
1. Click "☑ Chọn nhiều" button (toolbar)
2. Checkboxes appear in profile list
3. Select multiple profiles
4. Select target providers (Codex, Kiro, GitHub, OpenRouter)
5. Click "🔐 Auto Login All" in bulk actions bar
6. Progress panel opens showing live status per profile-provider
7. Sequential login with 2s delay between each
8. Summary shown when complete
```

---

## UI Components

### 1. Multi-Select Toggle Button
**Location:** Toolbar (after "🔐 Credentials" button)

```xaml
<Button Content="☑ Chọn nhiều" 
        Command="{Binding ToggleMultiSelectModeCommand}"
        ToolTip="Bật/tắt chế độ chọn nhiều profiles">
    <Button.Style>
        <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsMultiSelectMode}" Value="True">
                    <Setter Property="Background" Value="{DynamicResource AccentSoftBrush}" />
                    <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>
```

---

### 2. Profile List Checkboxes
**Location:** `ProfileListItemStyle` in MainWindow.xaml

**Grid columns (add checkbox at start):**
```xaml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="Auto" /> <!-- NEW: Checkbox (28px) -->
    <ColumnDefinition Width="24" />   <!-- Index -->
    <ColumnDefinition Width="36" />   <!-- Avatar -->
    <ColumnDefinition Width="*" />    <!-- Profile info -->
    <ColumnDefinition Width="Auto" /> <!-- Provider indicators (existing) -->
    <ColumnDefinition Width="Auto" /> <!-- Arrow › -->
</Grid.ColumnDefinitions>

<!-- NEW: Checkbox column -->
<CheckBox Grid.Column="0"
          IsChecked="{Binding IsSelected}"
          Visibility="{Binding DataContext.IsMultiSelectMode, 
                       RelativeSource={RelativeSource AncestorType=ListView},
                       Converter={StaticResource BoolToVisibilityConverter}}"
          VerticalAlignment="Center"
          Margin="8,0,4,0" />
```

---

### 3. Bulk Actions Bar
**Location:** Between toolbar and content area (Grid.Row="1")

```xaml
<Border Grid.Row="1" Grid.Column="1"
        Background="{DynamicResource AccentSoftBrush}"
        BorderBrush="{DynamicResource AccentBrush}"
        BorderThickness="0,0,0,1"
        Padding="16,12"
        Visibility="{Binding HasSelectedProfiles, 
                     Converter={StaticResource BoolToVisibilityConverter}}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        
        <!-- Left: Selection summary -->
        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
            <TextBlock Text="✓" FontSize="16" Margin="0,0,8,0" 
                       Foreground="{DynamicResource AccentBrush}" />
            <TextBlock Text="{Binding SelectedProfilesCount}" 
                       FontSize="12" FontWeight="SemiBold" />
            <TextBlock Text=" profiles selected" 
                       FontSize="12" Margin="4,0,0,0" />
        </StackPanel>
        
        <!-- Right: Provider selection + Actions -->
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
            <!-- Target providers -->
            <TextBlock Text="Login to:" 
                       VerticalAlignment="Center" 
                       Margin="0,0,8,0" />
            
            <StackPanel Orientation="Horizontal" Spacing="4">
                <CheckBox Content="Codex" 
                          IsChecked="{Binding BatchTargetCodex}" />
                <CheckBox Content="Kiro" 
                          IsChecked="{Binding BatchTargetKiro}" />
                <CheckBox Content="GitHub" 
                          IsChecked="{Binding BatchTargetGitHub}" />
                <CheckBox Content="OpenRouter" 
                          IsChecked="{Binding BatchTargetOpenRouter}" />
            </StackPanel>
            
            <Border Width="1" Background="{DynamicResource BorderBrush}" 
                    Margin="8,0" />
            
            <Button Content="🔐 Auto Login All" 
                    Command="{Binding StartBatchAutoLoginCommand}"
                    IsEnabled="{Binding HasTargetProviders}"
                    Padding="12,6" />
            
            <Button Content="Clear Selection" 
                    Command="{Binding ClearSelectionCommand}"
                    Padding="12,6" />
        </StackPanel>
    </Grid>
</Border>
```

---

### 4. Batch Progress Panel
**Location:** Overlay on main content area

```xaml
<!-- Overlay -->
<Border Grid.Row="2" Grid.Column="1"
        Background="#CC000000"
        Visibility="{Binding IsBatchLoginRunning, 
                     Converter={StaticResource BoolToVisibilityConverter}}">
    
    <!-- Centered modal -->
    <Border Width="700" Height="500" 
            Background="{DynamicResource SurfaceBrush}"
            CornerRadius="12"
            Padding="24"
            VerticalAlignment="Center"
            HorizontalAlignment="Center">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />  <!-- Header -->
                <RowDefinition Height="*" />     <!-- Progress list -->
                <RowDefinition Height="Auto" />  <!-- Actions -->
            </Grid.RowDefinitions>
            
            <!-- Header -->
            <StackPanel>
                <TextBlock Text="Batch Auto Login Progress" 
                           FontSize="18" FontWeight="SemiBold" />
                <TextBlock Text="{Binding BatchProgressSummary}" 
                           FontSize="12" 
                           Foreground="{DynamicResource MutedTextBrush}"
                           Margin="0,4,0,0" />
            </StackPanel>
            
            <!-- Progress List -->
            <ScrollViewer Grid.Row="1" Margin="0,16,0,16">
                <ItemsControl ItemsSource="{Binding BatchProgressRows}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Padding="12" 
                                    BorderThickness="0,0,0,1"
                                    BorderBrush="{DynamicResource BorderBrush}">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="32" />   <!-- Icon -->
                                        <ColumnDefinition Width="180" />  <!-- Profile -->
                                        <ColumnDefinition Width="100" />  <!-- Provider -->
                                        <ColumnDefinition Width="*" />    <!-- Message -->
                                        <ColumnDefinition Width="60" />   <!-- Duration -->
                                    </Grid.ColumnDefinitions>
                                    
                                    <TextBlock Text="{Binding StatusIcon}" 
                                               FontSize="18" />
                                    
                                    <TextBlock Grid.Column="1" 
                                               Text="{Binding ProfileName}" 
                                               FontSize="12" 
                                               FontWeight="SemiBold"
                                               TextTrimming="CharacterEllipsis" />
                                    
                                    <TextBlock Grid.Column="2" 
                                               Text="{Binding ProviderName}" 
                                               FontSize="11"
                                               Foreground="{DynamicResource MutedTextBrush}" />
                                    
                                    <TextBlock Grid.Column="3" 
                                               Text="{Binding StatusMessage}" 
                                               FontSize="11"
                                               Foreground="{DynamicResource MutedTextBrush}"
                                               TextTrimming="CharacterEllipsis" />
                                    
                                    <TextBlock Grid.Column="4" 
                                               Text="{Binding DurationText}" 
                                               FontSize="10"
                                               HorizontalAlignment="Right" />
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
            
            <!-- Actions -->
            <StackPanel Grid.Row="2" 
                        Orientation="Horizontal" 
                        HorizontalAlignment="Right"
                        Spacing="8">
                <Button Content="Stop Batch" 
                        Command="{Binding StopBatchLoginCommand}"
                        Visibility="{Binding IsBatchLoginRunning, 
                                     Converter={StaticResource BoolToVisibilityConverter}}"
                        Padding="12,6" />
                
                <Button Content="Close" 
                        Command="{Binding CloseBatchProgressCommand}"
                        Visibility="{Binding IsBatchLoginRunning, 
                                     Converter={StaticResource BoolToVisibilityConverter},
                                     ConverterParameter=Inverse}"
                        Padding="12,6" />
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
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
```

### BatchLoginProgressRow (new model)
```csharp
public class BatchLoginProgressRow : ViewModelBase
{
    public string ProfileName { get; }
    public ProviderKind Provider { get; }
    public string ProviderName => Provider.ToString();
    
    private BatchLoginState _state;
    public BatchLoginState State
    {
        get => _state;
        set
        {
            SetProperty(ref _state, value);
            OnPropertyChanged(nameof(StatusIcon));
        }
    }
    
    public string StatusIcon => State switch
    {
        BatchLoginState.Waiting => "⏸",
        BatchLoginState.InProgress => "⏳",
        BatchLoginState.Success => "✅",
        BatchLoginState.Failed => "❌",
        BatchLoginState.Skipped => "⊘",
        _ => "?"
    };
    
    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
    
    private TimeSpan _duration;
    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            SetProperty(ref _duration, value);
            OnPropertyChanged(nameof(DurationText));
        }
    }
    
    public string DurationText => Duration.TotalSeconds > 0 
        ? $"{Duration.TotalSeconds:F1}s" 
        : "";
    
    public BatchLoginProgressRow(string profileName, ProviderKind provider)
    {
        ProfileName = profileName;
        Provider = provider;
    }
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
private bool _isMultiSelectMode;
public bool IsMultiSelectMode
{
    get => _isMultiSelectMode;
    set => SetProperty(ref _isMultiSelectMode, value);
}

// Computed: Has selected profiles
public bool HasSelectedProfiles => 
    Profiles.Any(p => p.IsSelected);

public int SelectedProfilesCount => 
    Profiles.Count(p => p.IsSelected);

// Target providers
private bool _batchTargetCodex = true;
public bool BatchTargetCodex
{
    get => _batchTargetCodex;
    set
    {
        SetProperty(ref _batchTargetCodex, value);
        OnPropertyChanged(nameof(HasTargetProviders));
    }
}

private bool _batchTargetKiro = true;
public bool BatchTargetKiro
{
    get => _batchTargetKiro;
    set
    {
        SetProperty(ref _batchTargetKiro, value);
        OnPropertyChanged(nameof(HasTargetProviders));
    }
}

private bool _batchTargetGitHub;
public bool BatchTargetGitHub
{
    get => _batchTargetGitHub;
    set
    {
        SetProperty(ref _batchTargetGitHub, value);
        OnPropertyChanged(nameof(HasTargetProviders));
    }
}

private bool _batchTargetOpenRouter;
public bool BatchTargetOpenRouter
{
    get => _batchTargetOpenRouter;
    set
    {
        SetProperty(ref _batchTargetOpenRouter, value);
        OnPropertyChanged(nameof(HasTargetProviders));
    }
}

public bool HasTargetProviders => 
    BatchTargetCodex || BatchTargetKiro || BatchTargetGitHub || BatchTargetOpenRouter;

// Batch progress
private bool _isBatchLoginRunning;
public bool IsBatchLoginRunning
{
    get => _isBatchLoginRunning;
    set => SetProperty(ref _isBatchLoginRunning, value);
}

public ObservableCollection<BatchLoginProgressRow> BatchProgressRows { get; } 
    = new ObservableCollection<BatchLoginProgressRow>();

public string BatchProgressSummary
{
    get
    {
        var total = BatchProgressRows.Count;
        if (total == 0) return "";
        
        var completed = BatchProgressRows.Count(r => 
            r.State == BatchLoginState.Success || 
            r.State == BatchLoginState.Failed ||
            r.State == BatchLoginState.Skipped);
        var success = BatchProgressRows.Count(r => 
            r.State == BatchLoginState.Success);
        var failed = BatchProgressRows.Count(r => 
            r.State == BatchLoginState.Failed);
        var skipped = BatchProgressRows.Count(r => 
            r.State == BatchLoginState.Skipped);
        
        return $"{completed}/{total} completed · {success} success · {failed} failed · {skipped} skipped";
    }
}
```

### New Commands
```csharp
public ICommand ToggleMultiSelectModeCommand { get; }
public ICommand ClearSelectionCommand { get; }
public ICommand StartBatchAutoLoginCommand { get; }
public ICommand StopBatchLoginCommand { get; }
public ICommand CloseBatchProgressCommand { get; }

// In constructor:
ToggleMultiSelectModeCommand = new RelayCommand(ToggleMultiSelectMode);
ClearSelectionCommand = new RelayCommand(ClearSelection);
StartBatchAutoLoginCommand = new AsyncRelayCommand(StartBatchAutoLoginAsync);
StopBatchLoginCommand = new RelayCommand(StopBatchLogin);
CloseBatchProgressCommand = new RelayCommand(CloseBatchProgress);
```

### Core Logic
```csharp
private void ToggleMultiSelectMode()
{
    IsMultiSelectMode = !IsMultiSelectMode;
    
    // Clear selection when exiting multi-select mode
    if (!IsMultiSelectMode)
    {
        ClearSelection();
    }
}

private void ClearSelection()
{
    foreach (var profile in Profiles)
    {
        profile.IsSelected = false;
    }
}

private CancellationTokenSource? _batchLoginCts;

private async Task StartBatchAutoLoginAsync()
{
    var selectedProfiles = Profiles.Where(p => p.IsSelected).ToList();
    if (selectedProfiles.Count == 0) return;
    
    var targetProviders = new List<ProviderKind>();
    if (BatchTargetCodex) targetProviders.Add(ProviderKind.Codex);
    if (BatchTargetKiro) targetProviders.Add(ProviderKind.Kiro);
    if (BatchTargetGitHub) targetProviders.Add(ProviderKind.GitHub);
    if (BatchTargetOpenRouter) targetProviders.Add(ProviderKind.OpenRouter);
    
    if (targetProviders.Count == 0) return;
    
    // Setup
    IsBatchLoginRunning = true;
    BatchProgressRows.Clear();
    _batchLoginCts = new CancellationTokenSource();
    
    try
    {
        await RunBatchAutoLoginAsync(selectedProfiles, targetProviders, _batchLoginCts.Token);
        
        // Summary
        var summary = BatchProgressSummary;
        _logger.LogInformation("Batch login complete: {Summary}", summary);
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Batch login cancelled by user");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Batch auto login failed");
    }
    finally
    {
        IsBatchLoginRunning = false;
        _batchLoginCts?.Dispose();
        _batchLoginCts = null;
    }
}

private async Task RunBatchAutoLoginAsync(
    List<ProfileRowViewModel> profiles,
    List<ProviderKind> providers,
    CancellationToken ct)
{
    foreach (var profile in profiles)
    {
        foreach (var provider in providers)
        {
            if (ct.IsCancellationRequested) return;
            
            var progressRow = new BatchLoginProgressRow(profile.Profile.Name, provider)
            {
                State = BatchLoginState.InProgress,
                StatusMessage = "Checking credentials..."
            };
            
            // Add to UI (must be on UI thread)
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                BatchProgressRows.Add(progressRow);
            });
            
            var sw = Stopwatch.StartNew();
            
            try
            {
                // Check if credentials exist
                var hasCredentials = await _providerConnectionVault.HasCredentialsAsync(
                    profile.Profile.Name, 
                    provider, 
                    ct);
                
                if (!hasCredentials)
                {
                    progressRow.State = BatchLoginState.Skipped;
                    progressRow.StatusMessage = "No credentials configured";
                    progressRow.Duration = sw.Elapsed;
                    
                    _logger.LogInformation(
                        "Skipped {Profile} / {Provider} - no credentials",
                        profile.Profile.Name,
                        provider);
                    continue;
                }
                
                // Run auto login via orchestrator
                progressRow.StatusMessage = "Launching browser...";
                
                var startUri = GetProviderStartUri(provider);
                var timeout = TimeSpan.FromMinutes(2);
                
                var result = await _autoLoginOrchestrator.LoginAsync(
                    profile.Profile.Name,
                    provider,
                    startUri,
                    timeout,
                    ct);
                
                if (result.Success)
                {
                    progressRow.State = BatchLoginState.Success;
                    progressRow.StatusMessage = $"Logged in via {result.Method}";
                    
                    _logger.LogInformation(
                        "Auto login success: {Profile} / {Provider} via {Method} in {Duration:F1}s",
                        profile.Profile.Name,
                        provider,
                        result.Method,
                        sw.Elapsed.TotalSeconds);
                }
                else
                {
                    progressRow.State = BatchLoginState.Failed;
                    progressRow.StatusMessage = result.ErrorMessage ?? "Login failed";
                    
                    _logger.LogWarning(
                        "Auto login failed: {Profile} / {Provider} - {Error}",
                        profile.Profile.Name,
                        provider,
                        result.ErrorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                progressRow.State = BatchLoginState.Failed;
                progressRow.StatusMessage = "Cancelled";
                throw;
            }
            catch (Exception ex)
            {
                progressRow.State = BatchLoginState.Failed;
                progressRow.StatusMessage = ex.Message;
                
                _logger.LogError(
                    ex,
                    "Auto login error: {Profile} / {Provider}",
                    profile.Profile.Name,
                    provider);
                
                // Continue with next (don't throw)
            }
            finally
            {
                progressRow.Duration = sw.Elapsed;
                
                // Notify summary changed
                OnPropertyChanged(nameof(BatchProgressSummary));
            }
            
            // Delay between logins
            if (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);
            }
        }
    }
}

private Uri GetProviderStartUri(ProviderKind provider)
{
    return provider switch
    {
        ProviderKind.Codex => new Uri("https://chatgpt.com/"),
        ProviderKind.Kiro => new Uri("https://kiro.dev/"),
        ProviderKind.GitHub => new Uri("https://github.com/login"),
        ProviderKind.OpenRouter => new Uri("https://openrouter.ai/"),
        _ => new Uri("about:blank")
    };
}

private void StopBatchLogin()
{
    _batchLoginCts?.Cancel();
}

private void CloseBatchProgress()
{
    if (IsBatchLoginRunning) return; // Can't close while running
    
    BatchProgressRows.Clear();
    
    // Exit multi-select mode after batch
    IsMultiSelectMode = false;
}
```

---

## Implementation Phases

### Phase 1: Multi-Select UI (1-2h)
**Tasks:**
1. Add `IsMultiSelectMode` + commands to MainViewModel
2. Add checkbox column to profile list
3. Add bulk actions bar UI
4. Wire up selection tracking

**Files:**
- `MainWindow.xaml`
- `ViewModels/MainViewModel.cs`
- `ViewModels/ProfileRowViewModel.cs`

**Verify:** Toggle mode, check/uncheck profiles, bulk bar appears

---

### Phase 2: Batch Progress UI (1-2h)
**Tasks:**
1. Create `BatchLoginProgressRow` model
2. Add progress overlay to MainWindow.xaml
3. Add batch properties to MainViewModel
4. Wire up close command

**Files:**
- `Models/BatchLoginProgressRow.cs` (new)
- `MainWindow.xaml`
- `ViewModels/MainViewModel.cs`

**Verify:** Progress panel shows/hides, status icons render

---

### Phase 3: Batch Login Logic (3-4h)
**Tasks:**
1. Implement `RunBatchAutoLoginAsync()` with orchestrator
2. Add credential checking with `HasCredentialsAsync()`
3. Add error handling (continue on failure)
4. Add 2s delay between logins
5. Add cancellation support
6. Add logging

**Files:**
- `ViewModels/MainViewModel.cs`

**Verify:** Sequential login, auto-skip, continue-on-failure, cancellation

---

### Phase 4: Polish (1h)
**Tasks:**
1. Summary notifications
2. Keyboard shortcuts (Escape to exit)
3. Auto-exit multi-select after batch
4. Improve status messages

**Files:**
- `MainWindow.xaml`
- `ViewModels/MainViewModel.cs`

---

## Testing

### Manual Tests
- [ ] Multi-select mode toggle works
- [ ] Checkboxes toggle selection
- [ ] Bulk actions bar appears/disappears
- [ ] Provider checkboxes work
- [ ] Progress panel shows during batch
- [ ] Sequential login (2s delay between each)
- [ ] Auto-skip profiles without credentials
- [ ] Continue on failure (one fail doesn't stop batch)
- [ ] Cancellation works immediately
- [ ] Close button works after completion
- [ ] Summary is accurate
- [ ] Test with 1, 5, 10+ profiles

### Edge Cases
- [ ] All profiles have credentials
- [ ] No profiles have credentials
- [ ] Mixed credentials
- [ ] All succeed
- [ ] All fail
- [ ] Cancel at start
- [ ] Cancel mid-batch

---

## Dependencies

**Required Components (Already Implemented):**
- `AutoLoginOrchestrator` - Phase 4 ✅
- `ProviderConnectionVaultStore.HasCredentialsAsync()` - Phase 1 ✅
- `GoogleAccountVaultStore` - Phase 1 ✅
- Provider automations (Codex, Kiro, GitHub, OpenRouter) - Phases 2-3 ✅

**No new infrastructure needed** - only UI + orchestration logic.

---

## Future Enhancements

- Parallel login (N profiles in parallel)
- Retry failed logins
- Save profile selections as groups
- Export batch results to CSV
