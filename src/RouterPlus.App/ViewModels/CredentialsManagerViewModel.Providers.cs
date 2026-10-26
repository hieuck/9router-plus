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
    private CancellationTokenSource? _providerLoginCts;

    private ProviderConnectionRowViewModel? _selectedKiroConnection;
    private ProviderConnectionRowViewModel? _selectedGitHubConnection;
    private ProviderConnectionRowViewModel? _selectedOpenRouterConnection;
    public ObservableCollection<ProviderConnectionRowViewModel> KiroConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> GitHubConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> OpenRouterConnections { get; } = new();

    public ProviderConnectionRowViewModel? SelectedKiroConnection
    {
        get => _selectedKiroConnection;
        set
        {
            if (_selectedKiroConnection == value) return;
            _selectedKiroConnection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveKiroConnection));
            RemoveKiroConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    public ProviderConnectionRowViewModel? SelectedGitHubConnection
    {
        get => _selectedGitHubConnection;
        set
        {
            if (_selectedGitHubConnection == value) return;
            _selectedGitHubConnection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveGitHubConnection));
            RemoveGitHubConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    public ProviderConnectionRowViewModel? SelectedOpenRouterConnection
    {
        get => _selectedOpenRouterConnection;
        set
        {
            if (_selectedOpenRouterConnection == value) return;
            _selectedOpenRouterConnection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveOpenRouterConnection));
            RemoveOpenRouterConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    // Commands
    public AsyncRelayCommand<ProviderConnectionRowViewModel> SaveProviderRowCommand { get; }
    public AsyncRelayCommand<ProviderConnectionRowViewModel> LoginKiroRowCommand { get; }
    public AsyncRelayCommand<ProviderConnectionRowViewModel> LoginGitHubRowCommand { get; }
    public AsyncRelayCommand<ProviderConnectionRowViewModel> LoginOpenRouterRowCommand { get; }
    public AsyncRelayCommand RemoveKiroConnectionCommand { get; }
    public AsyncRelayCommand RemoveGitHubConnectionCommand { get; }
    public AsyncRelayCommand RemoveOpenRouterConnectionCommand { get; }

    public bool CanRemoveKiroConnection =>
        CanModifyCredentials && SelectedKiroConnection?.HasCredentials == true;

    public bool CanRemoveGitHubConnection =>
        CanModifyCredentials && SelectedGitHubConnection?.HasCredentials == true;

    public bool CanRemoveOpenRouterConnection =>
        CanModifyCredentials && SelectedOpenRouterConnection?.HasCredentials == true;

    private async Task LoadProviderConnectionsAsync()
    {
        CodexConnections.Clear();
        KiroConnections.Clear();
        GitHubConnections.Clear();
        OpenRouterConnections.Clear();

        foreach (var profile in _mainViewModel.FilteredProfiles)
        {
            var rowData = await _providerConnectionRowService.LoadAsync(profile, CancellationToken.None);

            // Load Codex with full inline editing support
            var codexConnection = rowData.CodexConnection;

            var codexRow = new CodexConnectionRowViewModel
            {
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                AuthMethod = codexConnection?.PreferredMethod ?? AuthMethod.GoogleOAuth,
                LinkedGoogleAccount = codexConnection?.LinkedGoogleAccount ?? string.Empty,
                Email = codexConnection?.DirectCredential?.Email ?? string.Empty,
                Password = codexConnection?.DirectCredential?.Password ?? string.Empty,
                TotpSecret = codexConnection?.DirectCredential?.TotpSecret ?? string.Empty,
                HasCredentials = codexConnection != null &&
                    (!string.IsNullOrEmpty(codexConnection.LinkedGoogleAccount) ||
                     codexConnection.DirectCredential != null),
                IsEditing = false,
                IsSelected = false
            };

            codexRow.SetConfiguredGoogleAccounts(ConfiguredGoogleAccounts);

            // Subscribe to property changes to update selection count
            codexRow.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(CodexConnectionRowViewModel.IsSelected)
                    or nameof(CodexConnectionRowViewModel.HasCredentials))
                {
                    OnPropertyChanged(nameof(CodexSelectedCount));
                    OnPropertyChanged(nameof(IsAllCodexSelected));
                    OnPropertyChanged(nameof(IsCodexSelectionIndeterminate));
                    ToggleSelectAllCodexCommand.RaiseCanExecuteChanged();
                }
            };

            CodexConnections.Add(codexRow);

            // Load other providers with full inline editing support
            foreach (var item in rowData.ProviderConnections)
            {
                var collection = item.Kind switch
                {
                    ProviderKind.Kiro => KiroConnections,
                    ProviderKind.GitHub => GitHubConnections,
                    ProviderKind.OpenRouter => OpenRouterConnections,
                    _ => null
                };
                if (collection is null)
                {
                    continue;
                }

                var connection = item.Connection;

                var row = new ProviderConnectionRowViewModel
                {
                    ProfileId = profile.Id,
                    ProfileName = profile.Name,
                    AuthMethod = connection?.PreferredMethod ?? AuthMethod.GoogleOAuth,
                    LinkedGoogleAccount = connection?.LinkedGoogleAccount ?? string.Empty,
                    Email = connection?.DirectCredential?.Email ?? string.Empty,
                    Password = connection?.DirectCredential?.Password ?? string.Empty,
                    TotpSecret = connection?.DirectCredential?.TotpSecret ?? string.Empty,
                    HasCredentials = connection != null &&
                        (!string.IsNullOrEmpty(connection.LinkedGoogleAccount) ||
                         connection.DirectCredential != null),
                    IsEditing = false,
                    IsSelected = false
                };

                collection.Add(row);
            }
        }
    }

    private async Task SaveProviderRowAsync(ProviderConnectionRowViewModel? row)
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
            SetStatus($"Editing provider credentials for {row.ProfileName}");
            return;
        }

        // Determine provider kind based on which collection contains this row
        ProviderKind provider;
        if (KiroConnections.Contains(row))
            provider = ProviderKind.Kiro;
        else if (GitHubConnections.Contains(row))
            provider = ProviderKind.GitHub;
        else if (OpenRouterConnections.Contains(row))
            provider = ProviderKind.OpenRouter;
        else
        {
            SetStatus("Cannot determine provider for this row");
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
                Provider = provider,
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
                    : null
            };

            await _providerConnectionVaultStore.SaveConnectionAsync(connection, CancellationToken.None);

            // Update UI state
            row.HasCredentials = true;
            row.IsEditing = false;

            SetStatus($"Saved {provider} credentials for {row.ProfileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving {provider} credentials: {ex.Message}");
        }
    }

    private Task RemoveKiroConnectionAsync()
    {
        return SelectedKiroConnection is { } row
            ? RemoveProviderConnectionAsync(row, ProviderKind.Kiro)
            : Task.CompletedTask;
    }

    private Task RemoveGitHubConnectionAsync()
    {
        return SelectedGitHubConnection is { } row
            ? RemoveProviderConnectionAsync(row, ProviderKind.GitHub)
            : Task.CompletedTask;
    }

    private Task RemoveOpenRouterConnectionAsync()
    {
        return SelectedOpenRouterConnection is { } row
            ? RemoveProviderConnectionAsync(row, ProviderKind.OpenRouter)
            : Task.CompletedTask;
    }

    private async Task LoginKiroRowAsync(ProviderConnectionRowViewModel? row)
    {
        await LoginProviderRowAsync(row, ProviderKind.Kiro);
    }

    private async Task LoginGitHubRowAsync(ProviderConnectionRowViewModel? row)
    {
        await LoginProviderRowAsync(row, ProviderKind.GitHub);
    }

    private async Task LoginOpenRouterRowAsync(ProviderConnectionRowViewModel? row)
    {
        await LoginProviderRowAsync(row, ProviderKind.OpenRouter);
    }

    private async Task LoginProviderRowAsync(ProviderConnectionRowViewModel? row, ProviderKind provider)
    {
        if (row == null || !row.HasCredentials)
        {
            SetStatus($"No {provider} credentials to login with");
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

        SetStatus($"🚀 Logging in {provider} for {row.ProfileName}...");

        _providerLoginCts?.Cancel();
        _providerLoginCts?.Dispose();
        _providerLoginCts = new CancellationTokenSource();
        var cancellationToken = _providerLoginCts.Token;

        try
        {
            if (string.IsNullOrWhiteSpace(row.Email) || string.IsNullOrWhiteSpace(row.Password))
            {
                SetStatus($"❌ {row.ProfileName}: Email and password required for Direct login");
                return;
            }

            var credential = new ProviderCredential
            {
                Email = row.Email.Trim(),
                Password = row.Password,
                TotpSecret = string.IsNullOrWhiteSpace(row.TotpSecret) ? null : row.TotpSecret.Trim()
            };

            Func<Task<string?>>? totpGenerator = null;
            if (!string.IsNullOrWhiteSpace(credential.TotpSecret))
            {
                var totpSecret = credential.TotpSecret!;
                totpGenerator = () => Task.FromResult<string?>(GoogleTotpGenerator.Generate(totpSecret, DateTimeOffset.UtcNow));
            }

            var result = await _credentialsProviderLoginService.LoginAsync(
                provider,
                profile,
                credential,
                totpGenerator,
                cancellationToken);

            if (result.Success)
            {
                SetStatus($"✓ {row.ProfileName}: {result.Message}");
            }
            else
            {
                SetStatus($"❌ {row.ProfileName}: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error connecting {provider}: {ex.Message}");
        }
    }

    private async Task RemoveProviderConnectionAsync(ProviderConnectionRowViewModel row, ProviderKind provider)
    {
        ArgumentNullException.ThrowIfNull(row);

        var profileName = row.ProfileName;

        try
        {
            await _providerConnectionVaultStore.RemoveConnectionAsync(
                profileName,
                provider,
                CancellationToken.None);

            // Update UI - clear credentials but keep row
            row.AuthMethod = AuthMethod.GoogleOAuth;
            row.LinkedGoogleAccount = string.Empty;
            row.Email = string.Empty;
            row.Password = string.Empty;
            row.TotpSecret = string.Empty;
            row.HasCredentials = false;
            row.IsEditing = false;

            // Update CanRemove properties
            OnPropertyChanged(nameof(CanRemoveKiroConnection));
            OnPropertyChanged(nameof(CanRemoveGitHubConnection));
            OnPropertyChanged(nameof(CanRemoveOpenRouterConnection));
            RemoveKiroConnectionCommand.RaiseCanExecuteChanged();
            RemoveGitHubConnectionCommand.RaiseCanExecuteChanged();
            RemoveOpenRouterConnectionCommand.RaiseCanExecuteChanged();

            SetStatus($"Removed {provider} credentials for {profileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error removing {provider} credentials: {ex.Message}");
        }
    }

}
