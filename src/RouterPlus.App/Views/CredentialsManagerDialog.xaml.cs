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

    private bool _isDisposed;
    private bool _isClosing;

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        UIEventLogger.LogDialogClose("CredentialsManager");

        if (_isDisposed)
        {
            return;
        }

        if (_isClosing)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _isClosing = true;
        try
        {
            await _viewModel.DisposeAsync();
            _isDisposed = true;
            Close();
        }
        finally
        {
            _isClosing = false;
        }
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        try
        {
            await _viewModel.DisposeAsync();
            _isDisposed = true;
            DialogResult = true;
            Close();
        }
        finally
        {
            _isClosing = false;
        }
    }

    private async void UnlockVault_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.UnlockVault");

        var passwordWindow = new Window
        {
            Title = "Unlock Google Vault",
            Width = 430,
            Height = 270,
            MinWidth = 430,
            MinHeight = 270,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13
        };

        var root = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(24, 20, 24, 20)
        };
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        var title = new System.Windows.Controls.TextBlock
        {
            Text = "Unlock Google Vault",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        System.Windows.Controls.Grid.SetRow(title, 0);
        root.Children.Add(title);

        var description = new System.Windows.Controls.TextBlock
        {
            Text = "Enter your vault password to access saved credentials.",
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
            Margin = new Thickness(0, 0, 0, 18)
        };
        System.Windows.Controls.Grid.SetRow(description, 1);
        root.Children.Add(description);

        var passwordLabel = new System.Windows.Controls.TextBlock
        {
            Text = "Vault password",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        System.Windows.Controls.Grid.SetRow(passwordLabel, 2);
        root.Children.Add(passwordLabel);

        var contentPanel = new System.Windows.Controls.StackPanel();
        System.Windows.Controls.Grid.SetRow(contentPanel, 3);

        var passwordBox = new System.Windows.Controls.PasswordBox
        {
            Name = "VaultPasswordBox",
            Height = 34,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 12)
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(passwordBox, "VaultPasswordBox");
        contentPanel.Children.Add(passwordBox);

        var rememberCheckBox = new System.Windows.Controls.CheckBox
        {
            Name = "RememberVaultCheckBox",
            Content = "Remember on this device",
            ToolTip = "Store an encrypted unlock key using Windows DPAPI.",
            Margin = new Thickness(0, 0, 0, 8)
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(rememberCheckBox, "RememberVaultCheckBox");
        contentPanel.Children.Add(rememberCheckBox);

        var hint = new System.Windows.Controls.TextBlock
        {
            Text = "The password is never stored directly.",
            FontSize = 10,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush")
        };
        contentPanel.Children.Add(hint);
        root.Children.Add(contentPanel);

        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        System.Windows.Controls.Grid.SetRow(buttonPanel, 4);

        var okButton = new System.Windows.Controls.Button
        {
            Content = "Unlock",
            Width = 88,
            Height = 34,
            IsDefault = true,
            Style = (System.Windows.Style)FindResource("PrimaryButtonStyle"),
            Margin = new Thickness(0, 0, 8, 0)
        };

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 88,
            Height = 34,
            IsCancel = true,
            Style = (System.Windows.Style)FindResource("DialogButtonStyle"),
            Margin = new Thickness(0)
        };

        okButton.Click += (s, args) =>
        {
            passwordWindow.DialogResult = true;
            passwordWindow.Close();
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        root.Children.Add(buttonPanel);
        passwordWindow.Content = root;

        var result = passwordWindow.ShowDialog();

        if (result != true || string.IsNullOrWhiteSpace(passwordBox.Password))
        {
            passwordBox.Clear();
            return;
        }

        try
        {
            await _viewModel.UnlockVaultAsync(passwordBox.Password, rememberCheckBox.IsChecked == true);
        }
        finally
        {
            passwordBox.Clear();
        }
    }
    private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GoogleAccountRowViewModel row)
        {
            row.TogglePasswordVisibility();
        }
    }

    private void ToggleTotpVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GoogleAccountRowViewModel row)
        {
            row.ToggleTotpSecretVisibility();
        }
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
        if (!_viewModel.CanRemoveGoogleAccount || _viewModel.SelectedGoogleAccount == null)
            return;

        var row = _viewModel.SelectedGoogleAccount;
        var profileName = row.ProfileName;
        var result = MessageBox.Show(
            $"Remove Google account for profile '{profileName}' from vault?\n\nThis will delete stored credentials for this profile.",
            "Remove Google Account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        await _viewModel.RemoveGoogleAccountAsync(row);
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

