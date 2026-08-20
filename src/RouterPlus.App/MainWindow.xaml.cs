using System.Windows;
using System.Windows.Controls;
using RouterPlus.Core.Providers;
using RouterPlus.App.ViewModels;
using WpfButton = System.Windows.Controls.Button;

namespace RouterPlus.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void ProfileList_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ViewModel.LaunchSelectedCommand.Execute(null);
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
        catch (Exception exception)
        {
            ViewModel.MarkApiKeyPasteFailed(card.Kind, exception.Message);
        }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ViewModel.LogText);
            ViewModel.MarkLogCopied();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Không thể sao chép log: {exception.Message}",
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

}
