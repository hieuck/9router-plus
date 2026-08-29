using RouterPlus.App.ViewModels;
using RouterPlus.App.Diagnostics;
using RouterPlus.Core;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace RouterPlus.App.Views;

public partial class CredentialsManagerDialog : Window
{
    private readonly CredentialsManagerViewModel _viewModel;

    public CredentialsManagerDialog(CredentialsManagerViewModel viewModel)
    {
        UIEventLogger.LogDialogOpen("CredentialsManager");
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        UIEventLogger.LogDialogClose("CredentialsManager");

        // Dispose vault session asynchronously
        _ = _viewModel.DisposeAsync();
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.DisposeAsync();
        DialogResult = true;
        Close();
    }

    private async void UnlockVault_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.UnlockVault");

        // Create simple password dialog
        var passwordWindow = new Window
        {
            Title = "Unlock Google Vault",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var stackPanel = new System.Windows.Controls.StackPanel
        {
            Margin = new Thickness(16)
        };

        stackPanel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Enter vault password:",
            Margin = new Thickness(0, 0, 0, 8)
        });

        var passwordBox = new System.Windows.Controls.PasswordBox
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        var rememberCheckBox = new System.Windows.Controls.CheckBox
        {
            Content = "Remember on this device (encrypted with Windows DPAPI)",
            Margin = new Thickness(0, 0, 0, 16)
        };

        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        var okButton = new System.Windows.Controls.Button
        {
            Content = "Unlock",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 80,
            IsCancel = true
        };

        okButton.Click += (s, args) =>
        {
            passwordWindow.DialogResult = true;
            passwordWindow.Close();
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        stackPanel.Children.Add(passwordBox);
        stackPanel.Children.Add(rememberCheckBox);
        stackPanel.Children.Add(buttonPanel);

        passwordWindow.Content = stackPanel;

        var result = passwordWindow.ShowDialog();

        if (result != true || string.IsNullOrWhiteSpace(passwordBox.Password))
        {
            return;
        }

        await _viewModel.UnlockVaultAsync(passwordBox.Password, rememberCheckBox.IsChecked == true);
    }

    // Google Account Management
    private void AddGoogleAccount_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.AddGoogleAccount");
        _viewModel.SetStatus("Feature coming soon: Add Google account");

        // TODO: Open dialog to add new Google account to vault
        // For now, user can use existing "Tự động đăng nhập Google" context menu
        MessageBox.Show(
            "To add a Google account, right-click on a profile and select 'Tự động đăng nhập Google'.",
            "Add Google Account",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void EditGoogleAccount_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.EditGoogleAccount");
        if (_viewModel.SelectedGoogleAccount == null)
            return;

        _viewModel.SetStatus($"Feature coming soon: Edit {_viewModel.SelectedGoogleAccount.Email}");

        // TODO: Open dialog to edit selected Google account
        MessageBox.Show(
            "To edit a Google account, right-click on a profile and select 'Tự động đăng nhập Google'.",
            "Edit Google Account",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void RemoveGoogleAccount_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.RemoveGoogleAccount");
        if (_viewModel.SelectedGoogleAccount == null)
            return;

        var email = _viewModel.SelectedGoogleAccount.Email;
        var result = MessageBox.Show(
            $"Remove Google account '{email}' from vault?\n\nThis will delete stored credentials for this account.",
            "Remove Google Account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        await _viewModel.RemoveGoogleAccountAsync(email);
    }

    // Codex Configuration
    private void ConfigureCodexConnection_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.ConfigureCodex");
        if (_viewModel.SelectedCodexConnection == null)
            return;

        var connection = _viewModel.SelectedCodexConnection;
        _viewModel.SetStatus($"Feature coming soon: Configure Codex for {connection.ProfileName}");

        // TODO: Open ProviderConnectionConfigDialog for Codex
        MessageBox.Show(
            $"Codex configuration for profile '{connection.ProfileName}':\n\n" +
            $"Current method: {connection.PreferredMethodText}\n" +
            $"Google account: {connection.LinkedGoogleAccount}\n" +
            $"Direct credentials: {(connection.HasDirectCredentials ? "Configured" : "Not configured")}\n\n" +
            "Full configuration UI coming soon.",
            "Configure Codex",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // Kiro Configuration
    private void ConfigureKiroConnection_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.ConfigureKiro");
        if (_viewModel.SelectedKiroConnection == null)
            return;

        var connection = _viewModel.SelectedKiroConnection;
        _viewModel.SetStatus($"Feature coming soon: Configure Kiro for {connection.ProfileName}");

        // TODO: Open ProviderConnectionConfigDialog for Kiro
        MessageBox.Show(
            $"Kiro configuration for profile '{connection.ProfileName}':\n\n" +
            $"Current method: {connection.PreferredMethodText}\n" +
            $"Google account: {connection.LinkedGoogleAccount}\n" +
            $"Direct credentials: {(connection.HasDirectCredentials ? "Configured" : "Not configured")}\n\n" +
            "Full configuration UI coming soon.",
            "Configure Kiro",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // GitHub Configuration
    private void ConfigureGitHubConnection_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.ConfigureGitHub");
        if (_viewModel.SelectedGitHubConnection == null)
            return;

        var connection = _viewModel.SelectedGitHubConnection;
        _viewModel.SetStatus($"Feature coming soon: Configure GitHub for {connection.ProfileName}");

        // TODO: Open ProviderConnectionConfigDialog for GitHub
        MessageBox.Show(
            $"GitHub configuration for profile '{connection.ProfileName}':\n\n" +
            $"Current method: {connection.PreferredMethodText}\n" +
            $"Google account: {connection.LinkedGoogleAccount}\n" +
            $"Direct credentials: {(connection.HasDirectCredentials ? "Configured" : "Not configured")}\n\n" +
            "Full configuration UI coming soon.",
            "Configure GitHub",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // OpenRouter Configuration
    private void ConfigureOpenRouterConnection_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.ConfigureOpenRouter");
        if (_viewModel.SelectedOpenRouterConnection == null)
            return;

        var connection = _viewModel.SelectedOpenRouterConnection;
        _viewModel.SetStatus($"Feature coming soon: Configure OpenRouter for {connection.ProfileName}");

        // TODO: Open ProviderConnectionConfigDialog for OpenRouter
        MessageBox.Show(
            $"OpenRouter configuration for profile '{connection.ProfileName}':\n\n" +
            $"Current method: {connection.PreferredMethodText}\n" +
            $"Google account: {connection.LinkedGoogleAccount}\n" +
            $"Direct credentials: {(connection.HasDirectCredentials ? "Configured" : "Not configured")}\n\n" +
            "Full configuration UI coming soon.",
            "Configure OpenRouter",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
