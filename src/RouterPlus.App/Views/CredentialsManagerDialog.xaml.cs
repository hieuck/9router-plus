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
