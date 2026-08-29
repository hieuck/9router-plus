using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using RouterPlus.App.ViewModels;

namespace RouterPlus.App.Views;

public partial class CredentialsManagerDialog : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _mainViewModel;
    private string _statusMessage = "Sử dụng context menu trên profile để manage Google credentials.\n\nProvider connections (GitHub, Codex, Kiro, OpenRouter) - Coming soon.";

    public CredentialsManagerDialog(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));

        InitializeComponent();
        DataContext = this;
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<GoogleAccountRowViewModel> GoogleAccounts { get; } = new();

    private void AddGoogleAccount_Click(object sender, RoutedEventArgs e)
    {
        StatusMessage = "Để thêm Google account: Right-click profile → 'Tự động đăng nhập Google'";
    }

    private void EditGoogleAccount_Click(object sender, RoutedEventArgs e)
    {
        // Not implemented yet
    }

    private void DeleteGoogleAccount_Click(object sender, RoutedEventArgs e)
    {
        // Not implemented yet
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class GoogleAccountRowViewModel
{
    public string Email { get; set; } = string.Empty;
    public string TotpIndicator { get; set; } = string.Empty;
}
