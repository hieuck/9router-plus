using RouterPlus.Infrastructure.Chrome;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace RouterPlus.App.Views;

public partial class ChromeSelectionDialog : Window, INotifyPropertyChanged
{
    private ChromeInstallationViewModel? _selectedInstallation;

    public ChromeSelectionDialog(IReadOnlyList<ChromeInstallation> installations)
    {
        InitializeComponent();
        DataContext = this;

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
        }
    }

    public ChromeInstallation? Result { get; private set; }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstallation != null)
        {
            Result = SelectedInstallation.Installation;
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
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
    }

    public ChromeInstallation Installation { get; }

    public string DisplayName { get; }

    public string ExecutablePath => Installation.ExecutablePath;

    public string UserDataDirectory => Installation.UserDataDirectory;

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
