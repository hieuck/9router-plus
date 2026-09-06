using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.App.ViewModels;
using RouterPlus.App.Views;
using RouterPlus.App.Testing;
using RouterPlus.App.Diagnostics;
using WpfButton = System.Windows.Controls.Button;

namespace RouterPlus.App;

public partial class MainWindow : Window
{
    private const double MinimumVisibleWindowSize = 64d;
    private bool _isClosing;
    private DiagnosticsWindow? _diagnosticsWindow;

    public MainWindow()
    {
        DataContext = HarnessEnvironment.IsEnabled
            ? new MainViewModel(
                settingsStore: HarnessEnvironment.CreateSettingsStore(),
                runStartupUpdateCheck: false,
                googleLoginVaultPaths: HarnessEnvironment.CreateGoogleAccountVaultPaths(),
                googleLoginAutomation: HarnessEnvironment.CreateGoogleLoginAutomation(),
                harnessProfiles: HarnessEnvironment.CreateProfiles())
            : new MainViewModel(runStartupUpdateCheck: true);
        ViewModel.LoadWindowPlacementSync();
        ApplySavedWindowPlacement();
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "MainWindow",
            "WindowLoaded",
            "Main window loaded, starting initialization",
            new { });

        // Initialize testing hooks for E2E automation
        TestingHooks.Initialize(ViewModel);

        await ViewModel.InitializeAsync();
        ViewModel.StartQuotaPolling();

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "MainWindow",
            "InitializationComplete",
            "Main window initialization complete",
            new { });
    }

    private async void Window_OnStateChanged(object? sender, EventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "MainWindow",
            "WindowStateChanged",
            "Window state changed",
            new { window_state = WindowState.ToString() });

        try
        {
            if (WindowState == WindowState.Minimized)
            {
                ViewModel.PauseQuotaPolling();
            }
            else if (WindowState == WindowState.Normal)
            {
                await ViewModel.ResumeQuotaPollingAsync();
            }
        }
        catch (OperationCanceledException)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "MainWindow",
                "StateTransitionCancelled",
                "Window state transition cancelled",
                new { });
        }
        catch (Exception exception)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "MainWindow",
                "StateTransitionFailed",
                $"Window state transition failed: {exception.Message}",
                new { error = exception.Message, error_type = exception.GetType().Name });
            // Swallow background polling errors to avoid crashing the app.
        }
    }

    private async void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        e.Cancel = true;
        _isClosing = true;
        try
        {
            await ViewModel.StopQuotaPollingAsync();
            await ViewModel.DisposeGoogleLoginSessionsAsync();
            if (WindowState == WindowState.Normal)
            {
                await ViewModel.SaveWindowPlacementAsync(Left, Top, Width, Height);
            }
        }
        finally
        {
            Close();
        }
    }

    private void ApplySavedWindowPlacement()
    {
        var placement = ViewModel.SavedWindowPlacement;
        if (placement is null
            || placement.Width < MinWidth
            || placement.Height < MinHeight
            || !IsPlacementVisible(placement))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = placement.Width;
        Height = placement.Height;
        Left = placement.Left;
        Top = placement.Top;
    }

    private static bool IsPlacementVisible(MainViewModel.WindowPlacement placement)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var windowRect = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        var visibleRect = Rect.Intersect(virtualScreen, windowRect);

        return !visibleRect.IsEmpty
            && visibleRect.Width >= MinimumVisibleWindowSize
            && visibleRect.Height >= MinimumVisibleWindowSize;
    }

    private void HelpMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton button && button.ContextMenu is not null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutWindow
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        // If already open, focus existing window
        if (_diagnosticsWindow != null)
        {
            try
            {
                // Check if window is still alive by accessing a property
                if (_diagnosticsWindow.IsVisible || _diagnosticsWindow.IsLoaded)
                {
                    _diagnosticsWindow.Activate();
                    _diagnosticsWindow.Focus();
                    return;
                }
            }
            catch
            {
                // Window was closed, create new one
                _diagnosticsWindow = null;
            }
        }

        // Create new window
        _diagnosticsWindow = new DiagnosticsWindow
        {
            Owner = this
        };

        // Clear reference when closed
        _diagnosticsWindow.Closed += (s, args) => _diagnosticsWindow = null;

        _diagnosticsWindow.Show();
    }

    private void OpenCredentialsManager_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("OpenCredentialsManager");
        try
        {
            var viewModel = new CredentialsManagerViewModel(
                ViewModel,
                ViewModel.GoogleAccountVaultStore,
                ViewModel.ProviderConnectionVaultStore,
                ViewModel.GoogleAccountVaultPaths,
                ViewModel.GoogleLoginAutomation,
                ViewModel.GoogleLoginHealthCheckAutomation,
                ViewModel.CodexLoginAutomation);
            var dialog = new CredentialsManagerDialog(viewModel)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to open Credentials Manager:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RunSetupWizard_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new WelcomeWizardWindow(new SettingsStore());
        var result = wizard.ShowDialog();

        if (result == true)
        {
            // User completed wizard, reload all settings
            await ViewModel.InitializeAsync();
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.InstallUpdateCommand.CanExecute(null) || ViewModel.AvailableVersion is null)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"Tải và cài bản {ViewModel.AvailableVersion} đã được xác minh? Ứng dụng sẽ đóng để hoàn tất cập nhật.",
            "Xác nhận cập nhật",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var started = await ViewModel.InstallUpdateAsync(confirmedByUser: true);
        if (!started)
        {
            System.Windows.MessageBox.Show(
                this,
                ViewModel.UpdateStatusText,
                "Cập nhật không thành công",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await ViewModel.SaveWindowPlacementAsync(Left, Top, Width, Height);
        _isClosing = true;
        Close();
    }

    private void ProfileList_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            // Only launch if double-click is on an actual profile item, not on empty space
            if (sender is not System.Windows.Controls.ListBox listBox || e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            if (ItemsControl.ContainerFromElement(listBox, source) is System.Windows.Controls.ListBoxItem { DataContext: ProfileRowViewModel row })
            {
                UIEventLogger.LogDoubleClick("ProfileListItem", row.Name);
                if (ViewModel.LaunchSelectedCommand.CanExecute(null))
                {
                    ViewModel.LaunchSelectedCommand.Execute(null);
                }
            }
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "MainWindow",
                "ProfileDoubleClickFailed",
                $"ProfileList double-click failed: {ex.Message}",
                new { error = ex.Message, error_type = ex.GetType().Name });

            System.Windows.MessageBox.Show(
                this,
                $"Lỗi khi mở profile:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                "Lỗi Double-Click",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ProfileList_OnPreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // No-op - performance measurement removed
    }

    private void ProfileList_OnContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            e.Handled = true;
            return;
        }

        // Use SelectedItem instead of slow ContainerFromElement lookup
        if (listBox.SelectedItem is ProfileRowViewModel row)
        {
            // Set DataContext for context menu bindings
            if (listBox.ContextMenu != null)
            {
                listBox.ContextMenu.DataContext = row;
            }
        }
        else
        {
            e.Handled = true;
        }
    }

    private async void ProfileGoogleLogin_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("ProfileGoogleLogin");

        // Get profile from context menu DataContext
        ChromeProfile? profile = null;
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is ProfileRowViewModel row)
        {
            profile = row.Profile;
        }
        else if (sender is System.Windows.Controls.MenuItem { Tag: ChromeProfile tagProfile })
        {
            profile = tagProfile;
        }

        if (profile == null)
        {
            await ViewModel.OpenSelectedGoogleLoginAsync();
            return;
        }

        var previousSelection = ViewModel.SelectedProfile;
        ViewModel.SelectedProfile = profile;
        await ViewModel.OpenSelectedGoogleLoginAsync();
        ViewModel.SelectedProfile = previousSelection;
    }

    private async void ProfileGoogleAutoLogin_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("ProfileGoogleAutoLogin");

        // Get profile from context menu DataContext
        ChromeProfile? profile = null;
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is ProfileRowViewModel row)
        {
            profile = row.Profile;
        }
        else if (sender is System.Windows.Controls.MenuItem { Tag: ChromeProfile tagProfile })
        {
            profile = tagProfile;
        }

        if (profile != null)
        {
            ViewModel.SelectedProfile = profile;
        }

        // Run auto-login directly without showing dialog
        await ViewModel.RunGoogleAutoLoginDirectAsync();
    }

    private void ProfileFolder_Click(object sender, RoutedEventArgs e)
    {
        ChromeProfile? profile = null;

        // Get profile from context menu DataContext
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is ProfileRowViewModel row)
        {
            profile = row.Profile;
        }
        else if (sender is System.Windows.Controls.MenuItem { Tag: ChromeProfile tagProfile })
        {
            profile = tagProfile;
        }
        else
        {
            profile = ViewModel.SelectedProfile;
        }

        if (profile is null)
        {
            return;
        }

        try
        {
            UIEventLogger.LogClick("ProfileFolder", profile.Name);
            if (!Directory.Exists(profile.ProfilePath))
            {
                throw new DirectoryNotFoundException($"Không tìm thấy thư mục profile: {profile.ProfilePath}");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = profile.ProfilePath,
                UseShellExecute = true
            });
            ViewModel.MarkProfileFolderOpened();
        }
        catch (Exception exception)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "MainWindow",
                "OpenProfileFolderFailed",
                $"Failed to open profile folder: {exception.Message}",
                new { error = exception.Message, error_type = exception.GetType().Name, profile_name = profile.Name });

            ViewModel.MarkProfileActionFailed(exception);
        }
    }

    private void CopyProfileName_Click(object sender, RoutedEventArgs e)
    {
        ChromeProfile? profile = null;

        // Get profile from context menu DataContext
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is ProfileRowViewModel row)
        {
            profile = row.Profile;
        }
        else if (sender is System.Windows.Controls.MenuItem { Tag: ChromeProfile tagProfile })
        {
            profile = tagProfile;
        }
        else
        {
            profile = ViewModel.SelectedProfile;
        }

        if (profile is null)
        {
            return;
        }

        try
        {
            UIEventLogger.LogClick("CopyProfileName", profile.Name);
            System.Windows.Clipboard.SetText(profile.Name);
            ViewModel.MarkProfileNameCopied();
        }
        catch (Exception exception)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "MainWindow",
                "CopyProfileNameFailed",
                $"Failed to copy profile name: {exception.Message}",
                new { error = exception.Message, error_type = exception.GetType().Name, profile_name = profile.Name });

            ViewModel.MarkProfileActionFailed(exception);
        }
    }

    private void CheckProfileHealth_Click(object sender, RoutedEventArgs e)
    {
        ProfileRowViewModel? row = null;

        // Get profile from context menu DataContext
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is ProfileRowViewModel menuRow)
        {
            row = menuRow;
        }

        if (row is null)
        {
            return;
        }

        try
        {
            UIEventLogger.LogClick("CheckProfileHealth", row.Profile.Name);
            if (ViewModel.CheckProfileHealthCommand.CanExecute(row))
            {
                ViewModel.CheckProfileHealthCommand.Execute(row);
            }
        }
        catch (Exception exception)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "MainWindow",
                "CheckProfileHealthFailed",
                $"Failed to check profile health: {exception.Message}",
                new { error = exception.Message, error_type = exception.GetType().Name, profile_name = row.Profile.Name });

            ViewModel.MarkProfileActionFailed(exception);
        }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        ChromeProfile? profile = null;

        // Get profile from context menu DataContext
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is ProfileRowViewModel row)
        {
            profile = row.Profile;
        }
        else if (sender is System.Windows.Controls.MenuItem { Tag: ChromeProfile tagProfile })
        {
            profile = tagProfile;
        }
        else
        {
            profile = ViewModel.SelectedProfile;
        }

        if (profile is null)
        {
            return;
        }

        UIEventLogger.LogClick("DeleteProfile", profile.Name);
        var result = System.Windows.MessageBox.Show(
            this,
            $"Bạn có chắc muốn xóa profile \"{profile.Name}\"?\n\nThư mục sẽ bị xóa:\n{profile.ProfilePath}\n\nChỉ thư mục profile này bị xóa; thư mục User Data vẫn được giữ lại.",
            "Xác nhận xóa profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "MainWindow",
                "DeleteProfileCancelled",
                "Delete profile cancelled by user",
                new { profile_name = profile.Name });
            return;
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "MainWindow",
            "DeletingProfile",
            "Deleting profile",
            new { profile_name = profile.Name, profile_path = profile.ProfilePath });

        await ViewModel.DeleteSelectedProfileAsync();
    }

    private async void ReenableQuotaConnection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { DataContext: QuotaResetSuggestion suggestion })
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"{suggestion.Message}\n\nBạn có muốn bật lại connection này không?",
            "Xác nhận bật lại connection",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await ViewModel.ReenableQuotaConnectionAsync(suggestion.ConnectionId, confirmedByUser: true);
    }

    private async void AddProviderApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.DataContext is not ProviderCardViewModel card)
        {
            return;
        }

        await ViewModel.AddApiKeyAsync(card.Kind, card.ApiKeyValue);
    }


    private void ToggleProviderApiKeyVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: ProviderCardViewModel card })
        {
            card.ToggleApiKeyVisibility();
        }
    }

    private void PasteProviderApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.DataContext is not ProviderCardViewModel card)
        {
            return;
        }

        try
        {
            var value = System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText().Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                ViewModel.MarkApiKeyPasteFailed(card.Kind);
                return;
            }

            card.ApiKeyValue = value;
            ViewModel.MarkApiKeyPasted(card.Kind);
        }
        catch (Exception)
        {
            ViewModel.MarkApiKeyPasteFailed(card.Kind);
        }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ViewModel.LogText);
            ViewModel.MarkLogCopied();
        }
        catch (Exception)
        {
            System.Windows.MessageBox.Show(
                this,
                "Không thể sao chép log. Kiểm tra quyền truy cập clipboard rồi thử lại.",
                "9Router Profile Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BrowseChrome_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn chrome.exe",
            Filter = "Chrome executable|chrome.exe|Executable files|*.exe|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.ChromeExecutablePath = dialog.FileName;
            ViewModel.RefreshProfiles();
        }
    }

    private void BrowseUserData_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Chọn thư mục Chrome User Data",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ViewModel.ChromeUserDataDirectory = dialog.SelectedPath;
            ViewModel.RefreshProfiles();
        }
    }

    private void DashboardPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            ViewModel.DashboardAuthPassword = passwordBox.Password;
        }
    }

}

