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
    public void RefreshProfiles()
    {
        var previousProfileId = SelectedProfile?.Id;

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Profiles",
            "ProfileRefreshStarted",
            "Starting profile discovery",
            new { harness_mode = _harnessMode, chrome_path = ChromeExecutablePath, user_data_dir = ChromeUserDataDirectory });

        if (_harnessMode)
        {
            RefreshHarnessProfiles(previousProfileId);
            return;
        }
        var discovery = _profileManager.DiscoverProfiles(
            string.IsNullOrWhiteSpace(ChromeExecutablePath) ? null : ChromeExecutablePath,
            string.IsNullOrWhiteSpace(ChromeUserDataDirectory) ? null : ChromeUserDataDirectory);
        _installation = discovery.Installation;

        if (_installation is null)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Profiles",
                "ChromeInstallationNotFound",
                "Profile refresh could not find Chrome installation",
                new { chrome_path = ChromeExecutablePath, user_data_dir = ChromeUserDataDirectory });
            return;
        }

        ChromeExecutablePath = _installation.ExecutablePath;
        ChromeUserDataDirectory = _installation.UserDataDirectory;
        var managedProfiles = _profileManager.ManagedProfiles
            .Where(profile => Directory.Exists(Path.Combine(profile.UserDataDirectory, profile.DirectoryName)))
            .ToArray();
        var profiles = discovery.Profiles;

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Profiles",
            "ProfileRefreshCompleted",
            "Profile discovery completed",
            new
            {
                total_profiles = profiles.Count,
                managed_profiles = managedProfiles.Length,
                user_data_dir = _installation.UserDataDirectory
            });

        Profiles.Clear();
        ProfileRows.Clear();

        // Compute ambiguous profile names once
        var allProfilesList = profiles.ToArray();
        var ambiguousNames = ProfileConnectionMatcher.GetAmbiguousProfileNames(allProfilesList);

        foreach (var profile in allProfilesList)
        {
            Profiles.Add(profile);
            var row = new ProfileRowViewModel(profile, Providers);
            row.SelectionChanged += OnProfileSelectionChanged;
            row.IsNameAmbiguous = ambiguousNames.Contains(ProfileConnectionMatcher.NormalizeName(profile.Name));
            ProfileRows.Add(row);
        }

        var restoredProfile = Profiles.FirstOrDefault(profile => profile.Id == previousProfileId)
            ?? Profiles.FirstOrDefault();
        ApplyProfileFilter();
        SelectedProfile = restoredProfile;
        OnPropertyChanged(nameof(SelectedProfileRow));
        UpdateProviderCardStatuses();
        LaunchSelectedCommand.RaiseCanExecuteChanged();
        OpenProviderDashboardCommand.RaiseCanExecuteChanged();
        TestConnectionCommand.RaiseCanExecuteChanged();
        WaitForConnectionCommand.RaiseCanExecuteChanged();
        CheckAllProfilesHealthCommand.RaiseCanExecuteChanged();
    }

    private void RefreshHarnessProfiles(string? previousProfileId)
    {
        var profiles = _harnessProfiles ?? Array.Empty<ChromeProfile>();
        Profiles.Clear();
        ProfileRows.Clear();

        var allProfilesList = profiles.ToArray();
        var ambiguousNames = ProfileConnectionMatcher.GetAmbiguousProfileNames(allProfilesList);

        foreach (var profile in allProfilesList)
        {
            Profiles.Add(profile);
            var row = new ProfileRowViewModel(profile, Providers);
            row.SelectionChanged += OnProfileSelectionChanged;
            row.IsNameAmbiguous = ambiguousNames.Contains(ProfileConnectionMatcher.NormalizeName(profile.Name));
            ProfileRows.Add(row);
        }

        ApplyProfileFilter();
        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == previousProfileId)
            ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedProfileRow));
        UpdateProviderCardStatuses();
        LaunchSelectedCommand.RaiseCanExecuteChanged();
        OpenProviderDashboardCommand.RaiseCanExecuteChanged();
        TestConnectionCommand.RaiseCanExecuteChanged();
        WaitForConnectionCommand.RaiseCanExecuteChanged();
        CheckAllProfilesHealthCommand.RaiseCanExecuteChanged();
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Chrome",
            "HarnessProfilesLoaded",
            "Harness profile refresh loaded synthetic profiles",
            new { profile_count = profiles.Count });
    }

    public async Task AddProfileAsync()
    {
        if (!CanAddProfile)
        {
            return;
        }

        try
        {
            _installation = _profileManager.FindInstallation(
                string.IsNullOrWhiteSpace(ChromeExecutablePath) ? null : ChromeExecutablePath,
                string.IsNullOrWhiteSpace(ChromeUserDataDirectory) ? null : ChromeUserDataDirectory);
            if (_installation is null)
            {
                throw new InvalidOperationException("Không tìm thấy Chrome User Data Directory. Hãy kiểm tra đường dẫn trong Cài đặt.");
            }

            var createdProfile = _profileProvisioner.Create(
                _installation.UserDataDirectory,
                ProfileSearchText,
                Profiles,
                _profileManager.ManagedProfiles);
            _profileManager.AddManaged(createdProfile);
            try
            {
                await _settingsStore.SaveAsync(BuildSettings());
            }
            catch
            {
                _profileManager.RemoveManaged(createdProfile);
                try
                {
                    var createdPath = Path.Combine(createdProfile.UserDataDirectory, createdProfile.DirectoryName);
                    if (Directory.Exists(createdPath))
                    {
                        Directory.Delete(createdPath, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup; managed state is already restored.
                }
                throw;
            }

            MarkSettingsSaved();
            RefreshProfiles();
            SelectedProfile = Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, ChromeProfile.CreateId(
                    createdProfile.UserDataDirectory,
                    createdProfile.DirectoryName),
                    StringComparison.Ordinal));
            StatusText = $"Đã thêm profile \"{createdProfile.Name}\" ({createdProfile.DirectoryName}).";
            ShowToast($"Đã thêm profile {createdProfile.Name}", ToastType.Success);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    public Task ClearProfileSearchAsync()
    {
        ProfileSearchText = string.Empty;
        return Task.CompletedTask;
    }

    public void MarkLogCopied() => StatusText = "Đã sao chép log vào clipboard.";

    public void MarkProfileNameCopied() => StatusText = "Đã sao chép tên profile vào clipboard.";

    public void MarkProfileFolderOpened()
    {
        if (SelectedProfile is not null)
        {
            StatusText = $"Đã mở thư mục profile {SelectedProfile.Name}.";
            ShowToast(StatusText, ToastType.Success);
        }
    }

    public void MarkProfileActionFailed(Exception exception, [CallerMemberName] string? operation = null) =>
        SetError(exception, operation);

    public void MarkApiKeyPasted(ProviderKind provider)
    {
        StatusText = $"{ProviderCatalog.Get(provider).DisplayName} API key đã được dán vào ô nhập.";
        ShowToast(StatusText, ToastType.Info);
    }

    public void MarkApiKeyPasteFailed(ProviderKind provider, string? details = null)
    {
        StatusText = string.IsNullOrWhiteSpace(details)
            ? $"Clipboard không có API key cho {ProviderCatalog.Get(provider).DisplayName}."
            : $"Không thể dán API key cho {ProviderCatalog.Get(provider).DisplayName}. Kiểm tra quyền truy cập clipboard.";
        ShowToast(StatusText, ToastType.Warning);
    }

    private void ApplyProfileFilter()
    {
        var selectedProfileId = SelectedProfile?.Id;
        FilteredProfiles.Clear();
        FilteredProfileRows.Clear();
        var rowsByProfileId = ProfileRows.ToDictionary(row => row.Profile.Id, StringComparer.Ordinal);
        var hasProviderFilter = ProviderFilterStates.Count > 0;
        var displayIndex = 1;
        foreach (var profile in ChromeProfileFilter.Filter(Profiles, ProfileSearchText))
        {
            if (!rowsByProfileId.TryGetValue(profile.Id, out var row))
            {
                continue;
            }
            if (IsUnassignedProfileFilterActive &&
                (row.ProviderStatuses.Any(status => !status.IsKnown) || row.ConnectedProviderCount > 0))
            {
                continue;
            }
            if (hasProviderFilter)
            {
                // Check all provider filters - ALL must match (AND logic)
                var passesAllFilters = true;
                foreach (var (kind, state) in ProviderFilterStates)
                {
                    var hasConnection = row.ProviderStatuses.Any(status =>
                        status.Definition.Kind == kind && status.IsConnected);

                    if (state == ProviderFilterState.Has && !hasConnection)
                    {
                        passesAllFilters = false;
                        break;
                    }
                    if (state == ProviderFilterState.NotHas && hasConnection)
                    {
                        passesAllFilters = false;
                        break;
                    }
                }

                if (!passesAllFilters)
                {
                    continue;
                }
            }
            FilteredProfiles.Add(profile);
            row.SetDisplayIndex(displayIndex++);
            FilteredProfileRows.Add(row);
        }

        if (selectedProfileId is not null &&
            FilteredProfiles.Any(profile => profile.Id == selectedProfileId))
        {
            SelectedProfile = Profiles.First(profile => profile.Id == selectedProfileId);
        }

        OnPropertyChanged(nameof(FilteredProfileCount));
        OnPropertyChanged(nameof(FilteredProfileCountLabel));
    }

    private void ToggleMultiSelectMode()
    {
        IsMultiSelectMode = !IsMultiSelectMode;
    }

    private void ClearSelection()
    {
        foreach (var row in ProfileRows)
        {
            row.IsSelected = false;
        }
        OnPropertyChanged(nameof(HasSelectedProfiles));
        OnPropertyChanged(nameof(SelectedProfilesText));
    }

    private void OnProfileSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasSelectedProfiles));
        OnPropertyChanged(nameof(SelectedProfilesText));
        OnPropertyChanged(nameof(AreAllProfilesSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        StartBatchAutoLoginCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Toggle select/deselect all profiles.
    /// If any profile is unselected → select all.
    /// If all profiles are selected → deselect all.
    /// </summary>
    private void ToggleSelectAll()
    {
        // Auto-enable multi-select mode
        if (!_isMultiSelectMode)
        {
            IsMultiSelectMode = true;
        }

        // Check if all are currently selected
        bool allSelected = ProfileRows.All(row => row.IsSelected);

        // Toggle: if all selected, deselect all; otherwise select all
        bool newState = !allSelected;
        foreach (var row in ProfileRows)
        {
            row.IsSelected = newState;
        }

        OnPropertyChanged(nameof(HasSelectedProfiles));
        OnPropertyChanged(nameof(SelectedProfilesText));
    }

    public bool AreAllProfilesSelected => ProfileRows.Any() && ProfileRows.All(row => row.IsSelected);

    public string SelectAllButtonText => AreAllProfilesSelected ? "☐  Bỏ chọn tất cả" : "☑  Chọn tất cả";

    /// <summary>
    /// Batch Phase 2: Check if a profile has vault credentials for any provider.
}

