using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.App.Setup;
using RouterPlus.App.Views;

namespace RouterPlus.App;

public partial class WelcomeWizardWindow : Window
{
    private static readonly HttpClient DefaultHttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly SettingsStore _settingsStore;
    private readonly ChromeLocator _chromeLocator = new();
    private readonly HttpClient _httpClient;
    private readonly NodeRouterSetupService _nodeRouterSetupService;
    private bool _routerVerified;
    private bool _chromeVerified;

    public WelcomeWizardWindow(
        SettingsStore? settingsStore = null,
        HttpClient? httpClient = null,
        NodeRouterSetupService? nodeRouterSetupService = null)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _httpClient = httpClient ?? DefaultHttpClient;
        _nodeRouterSetupService = nodeRouterSetupService ?? new NodeRouterSetupService();
        InitializeComponent();
    }

    public string DashboardUrl => DashboardUrlTextBox.Text.Trim();
    public string ChromeExecutablePath => ChromeExeTextBox.Text.Trim();
    public string ChromeUserDataDirectory => ChromeUserDataTextBox.Text.Trim();

    private async void CheckRouterButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckRouterAsync();
    }

    private async Task CheckRouterAsync()
    {
        var url = DashboardUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowRouterNotFound();
            return;
        }

        CheckRouterButton.IsEnabled = false;
        RouterCheckingPanel.Visibility = Visibility.Visible;
        RouterNotFoundPanel.Visibility = Visibility.Collapsed;
        RouterFoundPanel.Visibility = Visibility.Collapsed;

        try
        {
            // Just check if the URL is reachable (root endpoint)
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                ShowRouterFound();
            }
            else
            {
                ShowRouterNotFound($"9Router phản hồi mã HTTP {(int)response.StatusCode}. Kiểm tra lại Dashboard URL.");
            }
        }
        catch (HttpRequestException)
        {
            ShowRouterNotFound("Không thể kết nối tới 9Router. Hãy khởi chạy 9Router rồi thử lại.");
        }
        catch (TaskCanceledException)
        {
            ShowRouterNotFound("Kết nối tới 9Router quá thời gian chờ. Kiểm tra 9Router và Dashboard URL.");
        }
        finally
        {
            CheckRouterButton.IsEnabled = true;
            RouterCheckingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowRouterFound()
    {
        _routerVerified = true;
        RouterFoundPanel.Visibility = Visibility.Visible;
        RouterNotFoundPanel.Visibility = Visibility.Collapsed;
        UpdateSaveButtonState();
        SetupStatusText.Text = "9Router đã phản hồi. Bạn có thể lưu cấu hình khi Chrome hợp lệ.";
    }

    private void ShowRouterNotFound(string? message = null)
    {
        _routerVerified = false;
        RouterNotFoundPanel.Visibility = Visibility.Visible;
        RouterFoundPanel.Visibility = Visibility.Collapsed;
        UpdateSaveButtonState();
        RouterNotFoundMessageText.Text = message ?? "9Router chưa chạy hoặc URL không đúng.";
    }

    private async void CheckSetupButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshSetupStatusAsync();
    }

    private async Task RefreshSetupStatusAsync()
    {
        CheckSetupButton.IsEnabled = false;
        SetupStatusText.Text = "Đang kiểm tra Node.js, npm và 9Router...";
        NodeSetupActionsPanel.Visibility = Visibility.Collapsed;
        try
        {
            var status = await _nodeRouterSetupService.DetectAsync();
            SetupStatusText.Text = $"Node.js: {(status.NodeAvailable ? "✓" : "✗")}    npm: {(status.NpmAvailable ? "✓" : "✗")}    9Router: {(status.RouterAvailable ? "✓" : "✗")}";
            NodeSetupActionsPanel.Visibility = Visibility.Visible;
            InstallNodeButton.Visibility = status.NodeAvailable ? Visibility.Collapsed : Visibility.Visible;
            InstallRouterButton.Visibility = status.NpmAvailable && !status.RouterAvailable ? Visibility.Visible : Visibility.Collapsed;
            LaunchRouterButton.Visibility = status.RouterAvailable ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            CheckSetupButton.IsEnabled = true;
        }
    }

    private async void InstallNodeButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await _nodeRouterSetupService.EnsureNodeAsync();
        SetupStatusText.Text = result.Message;
        await RefreshSetupStatusAsync();
    }

    private async void InstallRouterButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await _nodeRouterSetupService.InstallRouterAsync();
        SetupStatusText.Text = result.Message;
        await RefreshSetupStatusAsync();
    }

    private async void LaunchRouterButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await _nodeRouterSetupService.LaunchRouterAsync();
        SetupStatusText.Text = result.Message;
        await CheckRouterAsync();
        await RefreshSetupStatusAsync();
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
            ChromeExeTextBox.Text = dialog.FileName;
            ValidateChromeConfig();
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
            ChromeUserDataTextBox.Text = dialog.SelectedPath;
            ValidateChromeConfig();
        }
    }

    private async void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        await TryAutoDetectChromeAsync();
    }

    private async Task TryAutoDetectChromeAsync()
    {
        AutoDetectButton.IsEnabled = false;
        AutoDetectButton.Content = "⏳ Đang phát hiện...";

        try
        {
            var installations = await Task.Run(() => _chromeLocator.FindAll());
            if (installations.Count > 0)
            {
                var dialog = new ChromeSelectionDialog(installations)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true && dialog.Result is not null)
                {
                    ChromeExeTextBox.Text = dialog.Result.ExecutablePath;
                    ChromeUserDataTextBox.Text = dialog.Result.UserDataDirectory;
                    ValidateChromeConfig();
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    this,
                    "Không tìm thấy Chrome trên máy.\n\nVui lòng chọn thủ công.",
                    "Tự động phát hiện",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        finally
        {
            AutoDetectButton.IsEnabled = true;
            AutoDetectButton.Content = "⚡ Tự động phát hiện Chrome";
        }
    }

    private void ValidateChromeConfig()
    {
        var exePath = ChromeExeTextBox.Text.Trim();
        var userDataPath = ChromeUserDataTextBox.Text.Trim();

        var exeValid = !string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath);
        var userDataValid = !string.IsNullOrWhiteSpace(userDataPath) && Directory.Exists(userDataPath);

        ChromeExeStatus.Text = exeValid ? "✓ Hợp lệ" : "✗ Không tìm thấy file";
        ChromeExeStatus.Foreground = exeValid
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.Red;

        ChromeUserDataStatus.Text = userDataValid ? "✓ Hợp lệ" : "✗ Không tìm thấy thư mục";
        ChromeUserDataStatus.Foreground = userDataValid
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.Red;

        _chromeVerified = exeValid && userDataValid;
        ChromeValidPanel.Visibility = _chromeVerified ? Visibility.Visible : Visibility.Collapsed;

        UpdateSaveButtonState();

        if (_routerVerified)
        {
            SetupStatusText.Text = "9Router đã phản hồi. Bạn có thể lưu cấu hình khi Chrome hợp lệ.";
        }
    }

    private void UpdateSaveButtonState()
    {
        SaveButton.IsEnabled = _routerVerified && _chromeVerified;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SaveButton.IsEnabled)
        {
            await SaveAndCloseAsync();
        }
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        // User chose to skip wizard
        DialogResult = false;
        Close();
    }

    private async Task SaveAndCloseAsync()
    {
        try
        {
            var settings = new RouterSettings(
                DashboardBaseUrl: DashboardUrlTextBox.Text.Trim(),
                ChromeExecutablePath: ChromeExeTextBox.Text.Trim(),
                ChromeUserDataDirectory: ChromeUserDataTextBox.Text.Trim(),
                UseLightTheme: true
            );

            await _settingsStore.SaveAsync(settings);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Lỗi khi lưu cài đặt:\n\n{ex.Message}",
                "Lỗi",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
