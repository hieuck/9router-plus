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
            // Already disposed, allow close without canceling
            return;
        }

        if (_isClosing)
        {
            // Still disposing, cancel this close attempt
            e.Cancel = true;
            return;
        }

        // First close attempt: cancel and dispose async
        e.Cancel = true;
        _isClosing = true;
        try
        {
            await _viewModel.DisposeAsync();
            _isDisposed = true;

            // Unsubscribe before final close to prevent re-entry
            Closing -= OnClosing;

            // Defer close to next UI cycle to avoid WPF visibility errors
            _ = Dispatcher.BeginInvoke(() => Close());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during CredentialsManager disposal: {ex}");
            _isClosing = false;
            throw;
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

    private void ToggleCodexPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CodexConnectionRowViewModel row)
        {
            row.IsPasswordVisible = !row.IsPasswordVisible;
        }
    }

    private void ToggleCodexTotpVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CodexConnectionRowViewModel row)
        {
            row.IsTotpSecretVisible = !row.IsTotpSecretVisible;
        }
    }

    private void ToggleKiroPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProviderConnectionRowViewModel row)
        {
            row.IsPasswordVisible = !row.IsPasswordVisible;
        }
    }

    private void ToggleKiroTotpVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProviderConnectionRowViewModel row)
        {
            row.IsTotpSecretVisible = !row.IsTotpSecretVisible;
        }
    }

    private void ToggleGitHubPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProviderConnectionRowViewModel row)
        {
            row.IsPasswordVisible = !row.IsPasswordVisible;
        }
    }

    private void ToggleGitHubTotpVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProviderConnectionRowViewModel row)
        {
            row.IsTotpSecretVisible = !row.IsTotpSecretVisible;
        }
    }

    private void ToggleOpenRouterPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProviderConnectionRowViewModel row)
        {
            row.IsPasswordVisible = !row.IsPasswordVisible;
        }
    }

    private void ToggleOpenRouterTotpVisibility_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProviderConnectionRowViewModel row)
        {
            row.IsTotpSecretVisible = !row.IsTotpSecretVisible;
        }
    }

    private void RemoveCodexConnection_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.RemoveCodexConnection");
        if (!_viewModel.CanRemoveCodexConnection || _viewModel.SelectedCodexConnection == null)
            return;

        var row = _viewModel.SelectedCodexConnection;
        var profileName = row.ProfileName;
        var result = MessageBox.Show(
            this,
            $"Remove Codex credentials for '{profileName}'?\n\nThis cannot be undone.",
            "Remove Codex Credentials",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        _viewModel.RemoveCodexConnectionCommand.Execute(null);
    }

    private void RemoveKiroConnection_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.RemoveKiroConnection");
        if (!_viewModel.CanRemoveKiroConnection || _viewModel.SelectedKiroConnection == null)
            return;

        var row = _viewModel.SelectedKiroConnection;
        var profileName = row.ProfileName;
        var result = MessageBox.Show(
            this,
            $"Remove Kiro credentials for '{profileName}'?\n\nThis cannot be undone.",
            "Remove Kiro Credentials",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        _viewModel.RemoveKiroConnectionCommand.Execute(null);
    }

    private void RemoveGitHubConnection_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.RemoveGitHubConnection");
        if (!_viewModel.CanRemoveGitHubConnection || _viewModel.SelectedGitHubConnection == null)
            return;

        var row = _viewModel.SelectedGitHubConnection;
        var profileName = row.ProfileName;
        var result = MessageBox.Show(
            this,
            $"Remove GitHub credentials for '{profileName}'?\n\nThis cannot be undone.",
            "Remove GitHub Credentials",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        _viewModel.RemoveGitHubConnectionCommand.Execute(null);
    }

    private void RemoveOpenRouterConnection_Click(object sender, RoutedEventArgs e)
    {
        UIEventLogger.LogClick("CredentialsManager.RemoveOpenRouterConnection");
        if (!_viewModel.CanRemoveOpenRouterConnection || _viewModel.SelectedOpenRouterConnection == null)
            return;

        var row = _viewModel.SelectedOpenRouterConnection;
        var profileName = row.ProfileName;
        var result = MessageBox.Show(
            this,
            $"Remove OpenRouter credentials for '{profileName}'?\n\nThis cannot be undone.",
            "Remove OpenRouter Credentials",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        _viewModel.RemoveOpenRouterConnectionCommand.Execute(null);
    }
}

