using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Core.Updates;
using RouterPlus.App;
using RouterPlus.App.Views;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Services;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Infrastructure.Updates;
using RouterPlus.App.Services;

namespace RouterPlus.App.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<ChromeProfile> Profiles { get; }

    public ObservableCollection<ChromeProfile> FilteredProfiles { get; }

    public ObservableCollection<ProfileRowViewModel> ProfileRows { get; }

    public ObservableCollection<ProfileRowViewModel> FilteredProfileRows { get; }

    public ObservableCollection<ChromeProfile> RecentProfilesList { get; } = new();

    public ObservableCollection<RecentProfileRowViewModel> RecentProfileRows { get; } = new();

    public ObservableCollection<ChromeProfile> FilteredQuickLaunchProfiles { get; } = new();

    public bool IsQuickLaunchFeatureEnabled => false;

    public bool IsQuickLaunchOpen
    {
        get => _isQuickLaunchOpen;
        private set
        {
            if (_isQuickLaunchOpen == value) return;
            _isQuickLaunchOpen = value;
            OnPropertyChanged();
        }
    }

    public string QuickLaunchFilterText
    {
        get => _quickLaunchFilterText;
        set
        {
            if (string.Equals(_quickLaunchFilterText, value, StringComparison.Ordinal)) return;
            _quickLaunchFilterText = value ?? string.Empty;
            OnPropertyChanged();
            RebuildQuickLaunchProfiles();
        }
    }

    public ChromeProfile? SelectedQuickLaunchProfile
    {
        get => _selectedQuickLaunchProfile;
        set
        {
            if (Equals(_selectedQuickLaunchProfile, value)) return;
            _selectedQuickLaunchProfile = value;
            OnPropertyChanged();
        }
    }

    public AsyncRelayCommand ClearRecentProfilesCommand { get; }
    public AsyncRelayCommand OpenQuickLaunchPaletteCommand { get; }
    public AsyncRelayCommand CloseQuickLaunchPaletteCommand { get; }
    public AsyncRelayCommand<ChromeProfile?> ConfirmQuickLaunchSelectionCommand { get; }
    public AsyncRelayCommand<int> MoveQuickLaunchSelectionCommand { get; }


    public IReadOnlyList<ProviderDefinition> Providers { get; }

    public IReadOnlyList<ProviderCardViewModel> ProviderCards { get; }

    public int ProviderCount => ProviderCards.Count;

    public IReadOnlyList<ProviderDefinition> ApiKeyProviders { get; }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set
        {
            if (string.Equals(_connectionStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _connectionStatusText = value;
            OnPropertyChanged();
        }
    }

    public ChromeProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (Equals(_selectedProfile, value))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "ViewModel",
                    "SelectedProfileUnchanged",
                    "SelectedProfile unchanged, skipping",
                    new { });
                return;
            }

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "ViewModel",
                "SelectedProfileChanging",
                "SelectedProfile changing",
                new { profile_name = value?.Name ?? "null" });
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedProfileRow));
            UpdateProviderCardStatuses();
            LaunchSelectedCommand.RaiseCanExecuteChanged();
            OpenProviderDashboardCommand.RaiseCanExecuteChanged();
            TestConnectionCommand.RaiseCanExecuteChanged();
            DeleteConnectionCommand.RaiseCanExecuteChanged();
            WaitForConnectionCommand.RaiseCanExecuteChanged();
            AutoGetKeyCommand.RaiseCanExecuteChanged();
            ConnectOpenRouterOAuthCommand.RaiseCanExecuteChanged();
            _ = LoadSelectedProfileApiKeysAsync();
        }
    }

    public void SelectProfileForContextMenu(ChromeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        SelectedProfile = profile;

        ObservabilityHub.Instance.IncrementCounter("profile.selected", tags: new Dictionary<string, string>
        {
            ["source"] = "context_menu"
        });
    }

    public ProfileRowViewModel? SelectedProfileRow => _selectedProfile is null

        ? null
        : ProfileRows.FirstOrDefault(row => row.Profile.Id == _selectedProfile.Id);

    public string ProfileSearchText
    {
        get => _profileSearchText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_profileSearchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _profileSearchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAddProfile));
            OnPropertyChanged(nameof(ProfileAddButtonText));
            OnPropertyChanged(nameof(CanClearProfileSearch));
            AddProfileCommand.RaiseCanExecuteChanged();
            ClearProfileSearchCommand.RaiseCanExecuteChanged();
            ApplyProfileFilter();
        }
    }

    public bool CanAddProfile =>
        !string.IsNullOrWhiteSpace(ProfileSearchText.Trim()) &&
        !Profiles.Any(profile => string.Equals(
            profile.Name.Trim(),
            ProfileSearchText.Trim(),
            StringComparison.OrdinalIgnoreCase));

    public string ProfileAddButtonText => string.IsNullOrWhiteSpace(ProfileSearchText.Trim())
        ? "Thêm profile"
        : $"Thêm profile \"{ProfileSearchText.Trim()}\"";

    public bool CanClearProfileSearch => !string.IsNullOrEmpty(ProfileSearchText);

    public bool IsMultiSelectMode
    {
        get => _isMultiSelectMode;
        set
        {
            if (_isMultiSelectMode == value)
            {
                return;
            }

            _isMultiSelectMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedProfiles));
            StartBatchAutoLoginCommand.RaiseCanExecuteChanged();

            // Clear selections when exiting multi-select mode
            if (!value)
            {
                foreach (var row in ProfileRows)
                {
                    row.IsSelected = false;
                }
            }
        }
    }

    public IEnumerable<ProfileRowViewModel> SelectedProfileRows =>
        ProfileRows.Where(row => row.IsSelected);

    public bool HasSelectedProfiles => _isMultiSelectMode && SelectedProfileRows.Any();

    public string SelectedProfilesText
    {
        get
        {
            var count = SelectedProfileRows.Count();
            return count == 1 ? "1 profile đã chọn" : $"{count} profiles đã chọn";
        }
    }

    // Batch Phase 3: Progress tracking
    public ObservableCollection<BatchLoginProgressRow> BatchProgressRows { get; } = new();

    private bool _isBatchLoginRunning;
    public bool IsBatchLoginRunning
    {
        get => _isBatchLoginRunning;
        set
        {
            if (_isBatchLoginRunning == value) return;
            _isBatchLoginRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BatchProgressSummary));
        }
    }

    public string BatchProgressSummary
    {
        get
        {
            var total = BatchProgressRows.Count;
            var completed = BatchProgressRows.Count(r =>
                r.State == BatchLoginState.Success ||
                r.State == BatchLoginState.Failed ||
                r.State == BatchLoginState.Skipped);
            var success = BatchProgressRows.Count(r => r.State == BatchLoginState.Success);
            var failed = BatchProgressRows.Count(r => r.State == BatchLoginState.Failed);
            return $"{completed}/{total} · {success} thành công · {failed} lỗi";
        }
    }

    public IReadOnlyList<ProfileProviderFilterOption> ProviderFilterOptions { get; private set; } = Array.Empty<ProfileProviderFilterOption>();

    private readonly Dictionary<ProviderKind, ProfileProviderFilterOption> _providerOptionByKind = new();

    private void InitializeProviderFilterOptions()
    {
        var providerOptions = ProviderCatalog.All.Select(definition => new ProfileProviderFilterOption(
            definition.Kind,
            definition.ShortDisplayName,
            definition.Glyph,
            $"Chỉ hiển profile có kết nối {definition.DisplayName}"));
        var options = providerOptions.ToArray();
        ProviderFilterOptions = options;
        foreach (var option in options)
        {
            if (option.Kind is { } kind)
            {
                _providerOptionByKind[kind] = option;
            }
        }
    }

    public HashSet<ProviderKind> SelectedProviderKinds { get; } = new();

    public Dictionary<ProviderKind, ProviderFilterState> ProviderFilterStates { get; } = new();

    public AsyncRelayCommand<ProviderKind> ToggleProviderCommand { get; }

    public AsyncRelayCommand ToggleUnassignedProfilesCommand { get; }

    public bool IsUnassignedProfileFilterActive { get; private set; }

    public void ToggleProvider(ProviderKind kind)
    {
        if (!_providerOptionByKind.TryGetValue(kind, out var option))
        {
            return;
        }

        // Cycle through 3 states: Off -> Has -> NotHas -> Off
        option.CycleFilterState();

        // Update dictionaries based on new state
        var newState = option.FilterState;
        if (newState == ProviderFilterState.Off)
        {
            SelectedProviderKinds.Remove(kind);
            ProviderFilterStates.Remove(kind);
        }
        else
        {
            SelectedProviderKinds.Add(kind);
            ProviderFilterStates[kind] = newState;
        }

        IsUnassignedProfileFilterActive = false;
        NotifyProviderFilterChanged();
    }

    public void ToggleUnassignedProfiles()
    {
        IsUnassignedProfileFilterActive = !IsUnassignedProfileFilterActive;
        if (IsUnassignedProfileFilterActive)
        {
            SelectedProviderKinds.Clear();
            ProviderFilterStates.Clear();
            foreach (var option in ProviderFilterOptions)
            {
                option.FilterState = ProviderFilterState.Off;
            }
        }

        NotifyProviderFilterChanged();
    }

    public void ClearProviderFilter()
    {
        if (SelectedProviderKinds.Count == 0 && !IsUnassignedProfileFilterActive)
        {
            return;
        }

        SelectedProviderKinds.Clear();
        ProviderFilterStates.Clear();
        IsUnassignedProfileFilterActive = false;
        foreach (var option in ProviderFilterOptions)
        {
            option.FilterState = ProviderFilterState.Off;
        }

        NotifyProviderFilterChanged();
    }

    public bool IsProviderFilterActive => SelectedProviderKinds.Count > 0 || IsUnassignedProfileFilterActive;

    public int FilteredProfileCount => FilteredProfileRows.Count;

    public string FilteredProfileCountLabel
    {
        get
        {
            var hasFilter = IsProviderFilterActive || !string.IsNullOrWhiteSpace(ProfileSearchText);
            return hasFilter
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} đang hiển thị", FilteredProfileCount)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} profile", FilteredProfileCount);
        }
    }

    private void NotifyProviderFilterChanged()
    {
        OnPropertyChanged(nameof(SelectedProviderKinds));
        OnPropertyChanged(nameof(IsProviderFilterActive));
        OnPropertyChanged(nameof(IsUnassignedProfileFilterActive));
        OnPropertyChanged(nameof(FilteredProfileCountLabel));
        ApplyProfileFilter();
    }

    private void UpdateProviderFilterCounts()
    {
        var rowsByProfileId = ProfileRows.ToDictionary(row => row.Profile.Id, StringComparer.Ordinal);
        foreach (var option in ProviderFilterOptions)
        {
            if (option.Kind is not { } kind)
            {
                continue;
            }

            var hasCount = ProfileRows.Count(row =>
                row.ProviderStatuses.Any(status =>
                    status.Definition.Kind == kind && status.IsConnected));
            option.SetProfileCounts(hasCount, ProfileRows.Count - hasCount);
            option.SetProfileCount(hasCount);
        }
    }

    public bool IsAppearanceSectionExpanded
    {
        get => _isAppearanceSectionExpanded;
        set
        {
            if (_isAppearanceSectionExpanded == value) return;
            _isAppearanceSectionExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsDashboardSectionExpanded
    {
        get => _isDashboardSectionExpanded;
        set
        {
            if (_isDashboardSectionExpanded == value) return;
            _isDashboardSectionExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsChromeSectionExpanded
    {
        get => _isChromeSectionExpanded;
        set
        {
            if (_isChromeSectionExpanded == value) return;
            _isChromeSectionExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsSettingsExpanded
    {
        get => _isSettingsExpanded;
        set
        {
            if (_isSettingsExpanded == value)
            {
                return;
            }

            _isSettingsExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsProfileSidebarCollapsed
    {
        get => _isProfileSidebarCollapsed;
        set
        {
            if (_isProfileSidebarCollapsed == value)
            {
                return;
            }

            _isProfileSidebarCollapsed = value;
            OnPropertyChanged();
        }
    }

}

