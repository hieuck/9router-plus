using RouterPlus.App.ViewModels;
using RouterPlus.Infrastructure.Security;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;

namespace RouterPlus.App.Views;

public partial class GoogleAutoLoginDialog : Window
{
    private readonly GoogleAutoLoginViewModel _viewModel;
    private readonly CancellationTokenSource _cts = new();

    public GoogleAutoLoginDialog(GoogleAutoLoginViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        InitializeComponent();
        Closing += OnClosing;

        // Subscribe to property changes to sync password fields
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.Password))
        {
            PasswordBox.Password = _viewModel.Password;
        }
        else if (e.PropertyName == nameof(_viewModel.TotpSecret))
        {
            TotpSecretBox.Password = _viewModel.TotpSecret;
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _cts.Cancel();
        await _viewModel.DisposeAsync();
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        var password = VaultPasswordBox.Password;
        var remember = RememberCheckBox.IsChecked ?? false;

        if (string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(
                "Please enter a vault password.",
                "Unlock Vault",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _viewModel.UnlockVaultAsync(password, remember, _cts.Token);
            VaultPasswordBox.Clear();
        }
        catch (OperationCanceledException)
        {
            // Dialog closing, ignore
        }
        catch
        {
            MessageBox.Show(
                "Failed to unlock vault. Please check your password.",
                "Unlock Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            VaultPasswordBox.Clear();
        }
    }

    private async void Lock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LockVaultAsync(_cts.Token);
            PasswordBox.Clear();
            TotpSecretBox.Clear();
        }
        catch (OperationCanceledException)
        {
            // Dialog closing, ignore
        }
        catch
        {
            MessageBox.Show(
                "Failed to lock vault.",
                "Lock Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailTextBox.Text;
        var password = PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : PasswordTextBox.Text;
        var totpSecret = TotpSecretBox.Visibility == Visibility.Visible ? TotpSecretBox.Password : TotpTextBox.Text;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(
                "Please fill in at least email and password.",
                "Save Information",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _viewModel.SaveInformationAsync(email, password, totpSecret, _cts.Token);
            // Don't clear - credentials are now displayed from ViewModel
        }
        catch (OperationCanceledException)
        {
            // Dialog closing, ignore
        }
        catch
        {
            MessageBox.Show(
                "Failed to save information.",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            PasswordBox.Clear();
            TotpSecretBox.Clear();
        }
    }

    private async void AutoLogin_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailTextBox.Text;
        var password = PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : PasswordTextBox.Text;
        var totpSecret = TotpSecretBox.Visibility == Visibility.Visible ? TotpSecretBox.Password : TotpTextBox.Text;

        if (string.IsNullOrWhiteSpace(email))
        {
            MessageBox.Show(
                "Please enter your email address.",
                "Auto Login",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(
                "Please enter your password.",
                "Auto Login",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // TOTP is optional - default to placeholder if not provided
        if (string.IsNullOrWhiteSpace(totpSecret))
        {
            totpSecret = "AAAAAAAAAAAAAAAAAAAAAAAA";
        }

        try
        {
            var result = await _viewModel.AutoLoginAsync(email, password, totpSecret, _cts.Token);

            // Clear immediately after use
            PasswordBox.Clear();
            TotpSecretBox.Clear();

            // Close dialog on success, leave open for manual intervention
            if (result.Category == Core.Security.GoogleLoginResultCategory.Success)
            {
                DialogResult = true;
                Close();
            }
            else if (result.Category == Core.Security.GoogleLoginResultCategory.ManualInterventionRequired)
            {
                MessageBox.Show(
                    "Manual intervention required. Chrome is open for you to continue.",
                    "Manual Intervention",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                // Leave dialog and Chrome open
            }
            else
            {
                MessageBox.Show(
                    "Auto-login failed. Please check your credentials or try again.",
                    "Auto-login Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (OperationCanceledException)
        {
            // Dialog closing, ignore
        }
        catch
        {
            MessageBox.Show(
                "Auto-login failed. Please try again.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            PasswordBox.Clear();
            TotpSecretBox.Clear();
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;

        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Google Vault Files (*.gvault)|*.gvault|All Files (*.*)|*.*",
            Title = "Import Vault"
        };

        if (openDialog.ShowDialog() != true)
            return;

        var passwordDialog = new Window
        {
            Title = "Import Password",
            Width = 350,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var passwordBox = new PasswordBox
        {
            Margin = new Thickness(20, 40, 20, 20)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 20),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom
        };

        okButton.Click += (s, args) => { passwordDialog.DialogResult = true; passwordDialog.Close(); };

        var grid = new Grid();
        grid.Children.Add(passwordBox);
        grid.Children.Add(okButton);
        passwordDialog.Content = grid;

        string importPassword = string.Empty;
        try
        {
            if (passwordDialog.ShowDialog() != true)
                return;

            importPassword = passwordBox.Password;

            if (string.IsNullOrWhiteSpace(importPassword))
                return;

            var confirmResult = MessageBox.Show(
                "This will replace your current vault and create a backup. Continue?",
                "Confirm Import",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
                return;

            await _viewModel.ImportAsync(openDialog.FileName, importPassword, _cts.Token);
            MessageBox.Show(
                "Vault imported successfully. Please unlock the new vault.",
                "Import Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            // Dialog closing, ignore
        }
        catch
        {
            MessageBox.Show(
                "Import failed. Please check the file and password.",
                "Import Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            passwordBox.Clear();
            importPassword = string.Empty;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Google Vault Files (*.gvault)|*.gvault|All Files (*.*)|*.*",
            Title = "Export Vault",
            DefaultExt = ".gvault",
            FileName = "google-login-vault-export.gvault"
        };

        if (saveDialog.ShowDialog() != true)
            return;

        var passwordDialog = new Window
        {
            Title = "Export Password",
            Width = 350,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var passwordBox = new PasswordBox
        {
            Margin = new Thickness(20, 40, 20, 20)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 20),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom
        };

        okButton.Click += (s, args) => { passwordDialog.DialogResult = true; passwordDialog.Close(); };

        var grid = new Grid();
        grid.Children.Add(passwordBox);
        grid.Children.Add(okButton);
        passwordDialog.Content = grid;

        string exportPassword = string.Empty;
        try
        {
            if (passwordDialog.ShowDialog() != true)
                return;

            exportPassword = passwordBox.Password;

            if (string.IsNullOrWhiteSpace(exportPassword))
                return;

            await _viewModel.ExportAsync(saveDialog.FileName, exportPassword, _cts.Token);
            MessageBox.Show(
                "Vault exported successfully.",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            // Dialog closing, ignore
        }
        catch
        {
            MessageBox.Show(
                "Export failed. Please try again.",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            passwordBox.Clear();
            exportPassword = string.Empty;
        }
    }

    private async void RemoveRemembered_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;

        var confirmResult = MessageBox.Show(
            "Remove remembered unlock for this device?",
            "Confirm Remove",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmResult != MessageBoxResult.Yes)
            return;

        try
        {
            await _viewModel.RemoveRememberedUnlockAsync(_cts.Token);
            MessageBox.Show(
                "Remembered unlock removed.",
                "Success",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            // Dialog closing, ignore
        }
        catch
        {
            MessageBox.Show(
                "Failed to remove remembered unlock.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ResetVault_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will permanently delete the vault and all stored credentials.\n\n" +
            "You will need to create a new vault with a new password.\n\n" +
            "This action cannot be undone. Continue?",
            "Reset Vault",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var vaultPaths = new GoogleLoginVaultPaths();
            if (File.Exists(vaultPaths.VaultPath))
                File.Delete(vaultPaths.VaultPath);
            if (File.Exists(vaultPaths.RememberedKeyPath))
                File.Delete(vaultPaths.RememberedKeyPath);

            MessageBox.Show(
                "Vault has been reset. Please enter a new password to create a new vault.",
                "Reset Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to reset vault: {ex.Message}",
                "Reset Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordBox.Visibility == Visibility.Visible)
        {
            // Switch to visible TextBox
            PasswordTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;
        }
        else
        {
            // Switch back to PasswordBox
            PasswordBox.Password = PasswordTextBox.Text;
            PasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
        }
    }

    private void ToggleTotpVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (TotpSecretBox.Visibility == Visibility.Visible)
        {
            // Switch to visible TextBox
            TotpTextBox.Text = TotpSecretBox.Password;
            TotpSecretBox.Visibility = Visibility.Collapsed;
            TotpTextBox.Visibility = Visibility.Visible;
        }
        else
        {
            // Switch back to PasswordBox
            TotpSecretBox.Password = TotpTextBox.Text;
            TotpTextBox.Visibility = Visibility.Collapsed;
            TotpSecretBox.Visibility = Visibility.Visible;
        }
    }
}
