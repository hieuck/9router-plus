using RouterPlus.Infrastructure.Chrome;
using RouterPlus.App.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace RouterPlus.App.Views;

public partial class ChromeSelectionDialog : Window, INotifyPropertyChanged
{
    private ChromeInstallationViewModel? _selectedInstallation;
    private readonly ChromeLocator _chromeLocator = new();

    public ChromeSelectionDialog(IReadOnlyList<ChromeInstallation> installations)
    {
        InitializeComponent();
        DataContext = this;

        LoadInstallations(installations);
    }

    private void LoadInstallations(IReadOnlyList<ChromeInstallation> installations)
    {
        Installations.Clear();
        foreach (var installation in installations)
        {
            Installations.Add(new ChromeInstallationViewModel(installation));
        }

        if (Installations.Count > 0)
        {
            SelectedInstallation = Installations[0];
        }
    }

    public ObservableCollection<ChromeInstallationViewModel> Installations { get; } = new();

    public ChromeInstallationViewModel? SelectedInstallation
    {
        get => _selectedInstallation;
        set
        {
            if (_selectedInstallation == value) return;
            _selectedInstallation = value;
            OnPropertyChanged();
            SelectButton.IsEnabled = value != null;
        }
    }

    public ChromeInstallation? Result { get; private set; }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("ChromeSelection.Select");
        if (SelectedInstallation != null)
        {
            Result = SelectedInstallation.Installation;
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("ChromeSelection.Cancel");
        DialogResult = false;
        Close();
    }

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Chrome, "ChromeSelection.Rescan");
        UIEventLogger.LogClick("ChromeSelection.Rescan");
        var installations = _chromeLocator.FindAll();
        DebugLogger.Log(DiagnosticCategories.Chrome, $"Chrome rescan found {installations.Count} installation(s)");
        LoadInstallations(installations);

        if (Installations.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Không tìm thấy Chrome installation nào.",
                "Scan lại",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void OpenChromeLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string path)
        {
            OpenFolderAndSelectFile(path);
        }
    }

    private void OpenUserDataLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string path)
        {
            OpenFolder(path);
        }
    }

    private static void OpenFolderAndSelectFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch
            {
                // Fallback: open folder
                var directory = Path.GetDirectoryName(filePath);
                if (directory != null)
                {
                    OpenFolder(directory);
                }
            }
        }
        else
        {
            System.Windows.MessageBox.Show($"File không tồn tại:\n{filePath}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenFolder(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            try
            {
                Process.Start("explorer.exe", $"\"{folderPath}\"");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Không thể mở thư mục:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            System.Windows.MessageBox.Show($"Thư mục không tồn tại:\n{folderPath}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ChromeInstallationViewModel
{
    public ChromeInstallationViewModel(ChromeInstallation installation)
    {
        Installation = installation;
        DisplayName = GetDisplayName(installation.ExecutablePath);
        IsValid = ValidateInstallation(installation);
    }

    public ChromeInstallation Installation { get; }

    public string DisplayName { get; }

    public string ExecutablePath => Installation.ExecutablePath;

    public string UserDataDirectory => Installation.UserDataDirectory;

    public bool IsValid { get; }

    private static bool ValidateInstallation(ChromeInstallation installation)
    {
        // Verify executable exists
        if (!File.Exists(installation.ExecutablePath))
        {
            return false;
        }

        // Verify User Data directory exists
        if (!Directory.Exists(installation.UserDataDirectory))
        {
            return false;
        }

        // Verify Local State file exists (confirms valid Chrome User Data)
        var localStatePath = Path.Combine(installation.UserDataDirectory, "Local State");
        if (!File.Exists(localStatePath))
        {
            return false;
        }

        return true;
    }

    private static string GetDisplayName(string executablePath)
    {
        if (executablePath.Contains("CentBrowser", StringComparison.OrdinalIgnoreCase))
        {
            return "CentBrowser";
        }
        if (executablePath.Contains("Google\\Chrome", StringComparison.OrdinalIgnoreCase))
        {
            return "Google Chrome";
        }
        if (executablePath.Contains("Chromium", StringComparison.OrdinalIgnoreCase))
        {
            return "Chromium";
        }
        if (executablePath.Contains("Brave", StringComparison.OrdinalIgnoreCase))
        {
            return "Brave Browser";
        }
        if (executablePath.Contains("Edge", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft Edge";
        }

        // Extract folder name before Application
        var parts = executablePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i].Equals("Application", StringComparison.OrdinalIgnoreCase) && i > 0)
            {
                return parts[i - 1];
            }
        }

        return "Chrome-based Browser";
    }
}
