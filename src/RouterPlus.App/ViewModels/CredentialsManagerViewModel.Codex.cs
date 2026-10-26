using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Core.Observability;
using RouterPlus.App.Services;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.App.ViewModels;

public sealed partial class CredentialsManagerViewModel
{
    private CodexConnectionRowViewModel? _selectedCodexConnection;
    public int CodexSelectedCount => CodexConnections.Count(c => c.IsSelected && c.HasCredentials);

    /// <summary>
    /// True if all Google accounts with credentials are selected.
    /// </summary>
    public bool IsAllCodexSelected
    {
        get
        {
            var credentialed = CodexConnections.Where(c => c.HasCredentials).ToList();
            return credentialed.Any() && credentialed.All(c => c.IsSelected);
        }
    }

    /// <summary>
    /// True if some (but not all) Codex connections with credentials are selected.
    /// </summary>
    public bool IsCodexSelectionIndeterminate
    {
        get
        {
            var credentialed = CodexConnections.Where(c => c.HasCredentials).ToList();
            var selectedCount = credentialed.Count(c => c.IsSelected);
            return selectedCount > 0 && selectedCount < credentialed.Count;
        }
    }

    public ObservableCollection<CodexConnectionRowViewModel> CodexConnections { get; } = new();
    public CodexConnectionRowViewModel? SelectedCodexConnection
    {
        get => _selectedCodexConnection;
        set
        {
            if (_selectedCodexConnection == value) return;
            _selectedCodexConnection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveCodexConnection));
            RemoveCodexConnectionCommand?.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand ToggleSelectAllCodexCommand { get; }

    // Codex commands
    public AsyncRelayCommand<CodexConnectionRowViewModel> SaveCodexRowCommand { get; }
    public AsyncRelayCommand<CodexConnectionRowViewModel> LoginCodexRowCommand { get; }
    public AsyncRelayCommand RemoveCodexConnectionCommand { get; }

    // Provider commands (Kiro, GitHub, OpenRouter)
    public bool CanRemoveCodexConnection =>
        CanModifyCredentials && SelectedCodexConnection?.HasCredentials == true;

    private void ToggleSelectAllCodex()
    {
        var credentialed = CodexConnections.Where(c => c.HasCredentials).ToList();
        if (!credentialed.Any()) return;

        var shouldSelect = !IsAllCodexSelected;
        foreach (var connection in credentialed)
        {
            connection.IsSelected = shouldSelect;
        }

        OnPropertyChanged(nameof(CodexSelectedCount));
        OnPropertyChanged(nameof(IsAllCodexSelected));
        OnPropertyChanged(nameof(IsCodexSelectionIndeterminate));
    }

    private async Task SaveCodexRowAsync(CodexConnectionRowViewModel? row)
    {
        if (row == null) return;

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        // Existing rows enter edit mode before the command saves changes
        if (row.HasCredentials && !row.IsEditing)
        {
            row.IsEditing = true;
            SetStatus($"Editing Codex credentials for {row.ProfileName}");
            return;
        }

        // Validate based on auth method
        if (row.AuthMethod == AuthMethod.GoogleOAuth)
        {
            if (string.IsNullOrWhiteSpace(row.LinkedGoogleAccount))
            {
                SetStatus("Google account is required for OAuth method");
                return;
            }

            // Verify the linked Google account exists
            var googleAccount = GoogleAccounts.FirstOrDefault(a =>
                a.Email.Equals(row.LinkedGoogleAccount, StringComparison.OrdinalIgnoreCase));
            if (googleAccount == null)
            {
                SetStatus($"Google account '{row.LinkedGoogleAccount}' not found in vault");
                return;
            }
        }
        else // Direct
        {
            if (string.IsNullOrWhiteSpace(row.Email))
            {
                SetStatus("Email is required for Direct login");
                return;
            }
            if (string.IsNullOrWhiteSpace(row.Password))
            {
                SetStatus("Password is required for Direct login");
                return;
            }
        }

        try
        {
            // Build connection
            var connection = new ProviderAuthConnection
            {
                ProfileName = row.ProfileName,
                Provider = ProviderKind.Codex,
                PreferredMethod = row.AuthMethod,
                LinkedGoogleAccount = row.AuthMethod == AuthMethod.GoogleOAuth
                    ? row.LinkedGoogleAccount
                    : null,
                DirectCredential = row.AuthMethod == AuthMethod.Direct
                    ? new ProviderCredential
                    {
                        Email = row.Email.Trim(),
                        Password = row.Password,
                        TotpSecret = string.IsNullOrWhiteSpace(row.TotpSecret)
                            ? null
                            : row.TotpSecret.Trim()
                    }
                    : (!string.IsNullOrWhiteSpace(row.TotpSecret)
                        ? new ProviderCredential
                        {
                            Email = string.Empty,
                            Password = string.Empty,
                            TotpSecret = row.TotpSecret.Trim()
                        }
                        : null)
            };

            await _providerConnectionVaultStore.SaveConnectionAsync(connection, CancellationToken.None);

            // Update UI state
            row.HasCredentials = true;
            row.IsEditing = false;

            SetStatus($"Saved Codex credentials for {row.ProfileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving Codex credentials: {ex.Message}");
        }
    }

    private Task RemoveCodexConnectionAsync()
    {
        return SelectedCodexConnection is { } row
            ? RemoveCodexConnectionAsync(row)
            : Task.CompletedTask;
    }

    private async Task RemoveCodexConnectionAsync(CodexConnectionRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var profileName = row.ProfileName;

        try
        {
            await _providerConnectionVaultStore.RemoveConnectionAsync(
                profileName,
                ProviderKind.Codex,
                CancellationToken.None);

            // Update UI - clear credentials but keep row
            row.AuthMethod = AuthMethod.GoogleOAuth;
            row.LinkedGoogleAccount = string.Empty;
            row.Email = string.Empty;
            row.Password = string.Empty;
            row.TotpSecret = string.Empty;
            row.HasCredentials = false;
            row.IsEditing = false;
            OnPropertyChanged(nameof(CanRemoveCodexConnection));
            RemoveCodexConnectionCommand.RaiseCanExecuteChanged();

            SetStatus($"Removed Codex credentials for {profileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error removing Codex credentials: {ex.Message}");
        }
    }

    private async Task LoginCodexRowAsync(CodexConnectionRowViewModel? row)
    {
        if (row == null || !row.HasCredentials)
        {
            SetStatus("No Codex credentials to login with");
            return;
        }

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        if (string.IsNullOrWhiteSpace(row.ProfileId))
        {
            SetStatus($"Cannot login {row.ProfileName}: Profile ID not resolved");
            return;
        }

        var profile = _mainViewModel.Profiles.FirstOrDefault(p => p.Id == row.ProfileId);
        if (profile == null)
        {
            SetStatus($"❌ {row.ProfileName}: Profile not found");
            return;
        }

        SetStatus($"🚀 Logging in Codex for {row.ProfileName}...");

        try
        {
            CodexLoginCredential credential;

            if (row.AuthMethod == AuthMethod.GoogleOAuth)
            {
                // Google OAuth flow: CodexOAuthAutomation uses existing Google session from the Chrome profile
                if (string.IsNullOrWhiteSpace(row.LinkedGoogleAccount))
                {
                    SetStatus($"❌ {row.ProfileName}: No linked Google account");
                    return;
                }

                var googleAccount = GoogleAccounts.FirstOrDefault(a =>
                    a.Email.Equals(row.LinkedGoogleAccount, StringComparison.OrdinalIgnoreCase));

                if (googleAccount == null)
                {
                    SetStatus($"❌ {row.ProfileName}: Google account '{row.LinkedGoogleAccount}' not found");
                    return;
                }

                // Validate that Google account is already logged in
                if (googleAccount.HealthStatus?.Status != CredentialHealthStatus.Healthy)
                {
                    SetStatus($"❌ {row.ProfileName}: Google account '{row.LinkedGoogleAccount}' must be logged in first. Please log in the Google account before using Codex OAuth.");
                    return;
                }

                SetStatus($"🚀 {row.ProfileName}: Starting Codex OAuth with Google account {row.LinkedGoogleAccount}...");

                credential = !string.IsNullOrWhiteSpace(row.TotpSecret)
                    ? CodexLoginCredential.FromGoogleOAuthWithTotp(row.ProfileId, row.LinkedGoogleAccount, row.TotpSecret.Trim())
                    : CodexLoginCredential.FromGoogleOAuth(row.ProfileId, row.LinkedGoogleAccount);
            }
            else // Direct method
            {
                // Direct login: auto-fill OpenAI login form
                if (string.IsNullOrWhiteSpace(row.Email) || string.IsNullOrWhiteSpace(row.Password))
                {
                    SetStatus($"❌ {row.ProfileName}: Email and password required for Direct login");
                    return;
                }

                credential = CodexLoginCredential.FromDirect(
                    row.ProfileId,
                    row.Email,
                    row.Password,
                    string.IsNullOrWhiteSpace(row.TotpSecret) ? null : row.TotpSecret.Trim());
            }

            // Step 2: Run Codex login automation
            var result = await _runCodexAuthentication(profile, credential, CancellationToken.None);

            if (result.Category == CodexLoginResultCategory.Success)
            {
                SetStatus($"✓ {row.ProfileName}: Codex login successful");
            }
            else if (result.Category == CodexLoginResultCategory.ManualInterventionRequired)
            {
                SetStatus($"⚠ {row.ProfileName}: Manual intervention required - {result.Message}");
            }
            else
            {
                SetStatus($"❌ {row.ProfileName}: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"❌ {row.ProfileName}: {ex.Message}");
        }
    }

}
