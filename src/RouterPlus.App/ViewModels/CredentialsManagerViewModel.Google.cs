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
    private GoogleAccountRowViewModel? _selectedGoogleAccount;
    private bool _isBatchLoginRunning;
    private CancellationTokenSource? _batchLoginCts;
    private Task? _batchLoginTask;
    public ObservableCollection<GoogleAccountRowViewModel> GoogleAccounts { get; } = new();

    /// <summary>
    /// Filtered view of Google accounts that have credentials configured.
    /// Used in ComboBox dropdowns to avoid showing empty entries.
    /// </summary>
    public IEnumerable<GoogleAccountRowViewModel> ConfiguredGoogleAccounts =>
        GoogleAccounts.Where(account => account.HasCredentials &&
                                       !string.IsNullOrWhiteSpace(account.Email));

    public int SelectedCount => GoogleAccounts.Count(a => a.IsSelected && a.HasCredentials);

    public bool IsAllGoogleSelected
    {
        get
        {
            var credentialed = GoogleAccounts.Where(a => a.HasCredentials).ToList();
            return credentialed.Any() && credentialed.All(a => a.IsSelected);
        }
    }

    /// <summary>
    /// True if some (but not all) Google accounts with credentials are selected.
    /// </summary>
    public bool IsGoogleSelectionIndeterminate
    {
        get
        {
            var credentialed = GoogleAccounts.Where(a => a.HasCredentials).ToList();
            var selectedCount = credentialed.Count(a => a.IsSelected);
            return selectedCount > 0 && selectedCount < credentialed.Count;
        }
    }

    /// <summary>
    /// True if all Codex connections with credentials are selected.
    /// </summary>
    public bool IsBatchLoginRunning
    {
        get => _isBatchLoginRunning;
        private set
        {
            if (_isBatchLoginRunning == value) return;
            _isBatchLoginRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanModifyCredentials));
            OnPropertyChanged(nameof(CanRemoveGoogleAccount));
            OnPropertyChanged(nameof(CanRemoveCodexConnection));
            OnPropertyChanged(nameof(CanRemoveKiroConnection));
            OnPropertyChanged(nameof(CanRemoveGitHubConnection));
            OnPropertyChanged(nameof(CanRemoveOpenRouterConnection));
            BatchLoginCommand.RaiseCanExecuteChanged();
            StopBatchLoginCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            CheckAllHealthCommand.RaiseCanExecuteChanged();
            SaveRowCommand.RaiseCanExecuteChanged();
            LoginRowCommand.RaiseCanExecuteChanged();
            CheckHealthRowCommand.RaiseCanExecuteChanged();
            RemoveGoogleAccountCommand.RaiseCanExecuteChanged();
            SaveCodexRowCommand.RaiseCanExecuteChanged();
            LoginCodexRowCommand.RaiseCanExecuteChanged();
            RemoveCodexConnectionCommand.RaiseCanExecuteChanged();
            SaveProviderRowCommand.RaiseCanExecuteChanged();
            LoginKiroRowCommand.RaiseCanExecuteChanged();
            LoginGitHubRowCommand.RaiseCanExecuteChanged();
            LoginOpenRouterRowCommand.RaiseCanExecuteChanged();
            RemoveKiroConnectionCommand.RaiseCanExecuteChanged();
            RemoveGitHubConnectionCommand.RaiseCanExecuteChanged();
            RemoveOpenRouterConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    public GoogleAccountRowViewModel? SelectedGoogleAccount
    {
        get => _selectedGoogleAccount;
        set
        {
            if (_selectedGoogleAccount == value) return;
            _selectedGoogleAccount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveGoogleAccount));
            RemoveGoogleAccountCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanRemoveGoogleAccount =>
        CanModifyCredentials && SelectedGoogleAccount?.HasCredentials == true;

    // Provider connections per provider
    public AsyncRelayCommand<GoogleAccountRowViewModel> SaveRowCommand { get; }
    public AsyncRelayCommand<GoogleAccountRowViewModel> LoginRowCommand { get; }
    public AsyncRelayCommand<GoogleAccountRowViewModel> CheckHealthRowCommand { get; }
    public AsyncRelayCommand RemoveGoogleAccountCommand { get; }
    public AsyncRelayCommand BatchLoginCommand { get; }
    public RelayCommand StopBatchLoginCommand { get; }
    public AsyncRelayCommand CheckAllHealthCommand { get; }
    public RelayCommand ToggleSelectAllGoogleCommand { get; }
    public Task? BatchLoginTask => _batchLoginTask;

    private async Task LoadProfileRowsAsync(GoogleAccountVaultSession? session)
    {
        await Task.Yield();

        GoogleAccounts.Clear();

        // Create row for each profile
        foreach (var profile in _mainViewModel.FilteredProfiles)
        {
            var credential = ResolveCredentialForProfile(session?.Vault, profile);

            var row = new GoogleAccountRowViewModel
            {
                ProfileName = profile.Name,
                ProfileId = profile.Id,
                Email = credential?.Email ?? string.Empty,
                Password = credential?.Password ?? string.Empty,
                TotpSecret = credential?.TotpSecret ?? string.Empty,
                HasCredentials = credential != null,
                IsEditing = false,
                IsSelected = false,
                IsVaultUnlocked = session != null
            };

            // Subscribe to property changes to update SelectedCount
            row.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(GoogleAccountRowViewModel.IsSelected)
                    or nameof(GoogleAccountRowViewModel.HasCredentials))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(ConfiguredGoogleAccounts));
                    OnPropertyChanged(nameof(IsAllGoogleSelected));
                    OnPropertyChanged(nameof(IsGoogleSelectionIndeterminate));
                    BatchLoginCommand.RaiseCanExecuteChanged();
                    ToggleSelectAllGoogleCommand.RaiseCanExecuteChanged();
                }
                else if (e.PropertyName is nameof(GoogleAccountRowViewModel.Email))
                {
                    OnPropertyChanged(nameof(ConfiguredGoogleAccounts));
                }
            };

            GoogleAccounts.Add(row);
        }

        OnPropertyChanged(nameof(ConfiguredGoogleAccounts));
    }

    /// <summary>
    /// Resolves the credential for a profile using the stable profile Id first,
    /// then falls back to the legacy display-name key ONLY when the name maps
    /// unambiguously to a single current profile (no silent merge of shared names).
    /// A load never writes to the vault: legacy name-keyed records stay on disk
    /// until an explicit save migrates them to the stable Id.
    /// </summary>
    private GoogleLoginCredential? ResolveCredentialForProfile(GoogleAccountVault? vault, ChromeProfile profile)
    {
        if (vault is null)
        {
            return null;
        }

        var byId = vault.Find(profile.Id);
        if (byId is not null)
        {
            return byId;
        }

        // Legacy compatibility: adopt a name-keyed record only when exactly one
        // current profile carries that display name.
        return HasUniqueProfileName(profile.Name) ? vault.Find(profile.Name) : null;
    }

    private bool HasUniqueProfileName(string profileName)
    {
        return _mainViewModel.Profiles.Count(p => p.Name == profileName) == 1;
    }

    /// <summary>
    /// Resolves the ChromeProfile backing a row by stable Id first, falling
    /// back to the display name for legacy rows created before Id resolution.
    /// </summary>
    private ChromeProfile? ResolveRowProfile(GoogleAccountRowViewModel row)
    {
        if (!string.IsNullOrEmpty(row.ProfileId))
        {
            return _mainViewModel.Profiles.FirstOrDefault(p => p.Id == row.ProfileId);
        }

        return HasUniqueProfileName(row.ProfileName)
            ? _mainViewModel.Profiles.FirstOrDefault(p => p.Name == row.ProfileName)
            : null;
    }

    private async Task SaveRowAsync(GoogleAccountRowViewModel? row)
    {
        if (row == null) return;

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        // Existing rows enter edit mode before the command saves changes.
        if (row.HasCredentials && !row.IsEditing)
        {
            row.IsEditing = true;
            SetStatus($"Editing credentials for {row.ProfileName}");
            return;
        }

        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked");
            return;
        }

        // Validate
        if (string.IsNullOrWhiteSpace(row.Email))
        {
            SetStatus("Email is required");
            return;
        }
        if (string.IsNullOrWhiteSpace(row.Password))
        {
            SetStatus("Password is required");
            return;
        }

        using var trace = TraceScope.Begin("CredentialsManager", "SaveCredential", new { profile = row.ProfileName, has_totp = !string.IsNullOrWhiteSpace(row.TotpSecret) });

        try
        {
            // Resolve the underlying profile: by stable Id first, then by display
            // name so legacy rows created before Id resolution still work. Write
            // the resolved Id back onto the row for future operations.
            var profile = ResolveRowProfile(row);
            if (profile is null)
            {
                ObservabilityHub.Instance.IncrementCounter("credentials.save.failed", tags: new Dictionary<string, string> { ["reason"] = "profile_not_found" });
                SetStatus($"Profile not found for {row.ProfileName}");
                return;
            }

            row.ProfileId = profile.Id;

            // Create credential keyed by the stable profile Id (TOTP required - use placeholder if empty).
            var credential = new GoogleLoginCredential(
                row.ProfileId,
                row.Email.Trim(),
                row.Password,
                string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

            trace.LogCheckpoint("SavingToVault");

            // Upsert into vault (immutable pattern). Remove any prior record keyed
            // by the stable Id AND any legacy record keyed by the display name,
            // but only remove the name-keyed record when the name maps
            // unambiguously to this profile (never merge shared names).
            var currentVault = _vaultSession.Vault;
            var filtered = currentVault.Records
                .Where(r => r.ProfileId != row.ProfileId)
                .Where(r => !HasUniqueProfileName(row.ProfileName) || r.ProfileId != row.ProfileName);
            var updated = filtered.Append(credential);
            var newVault = new GoogleAccountVault(updated);
            _vaultSession.Replace(newVault);
            await _googleAccountVaultStore.SaveAsync(_vaultSession, CancellationToken.None);

            // Update UI state
            row.HasCredentials = true;
            row.IsEditing = false;

            ObservabilityHub.Instance.IncrementCounter("credentials.save.success");
            OnPropertyChanged(nameof(ConfiguredGoogleAccounts));
            SetStatus($"Saved credentials for {row.ProfileName}");
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.IncrementCounter("credentials.save.failed", tags: new Dictionary<string, string> { ["reason"] = "exception" });
            ObservabilityHub.Instance.LogEvent(LogLevel.Error, "CredentialsManager", "SaveCredentialFailed",
                "Failed to save credential", new { profile = row.ProfileName, error = ex.Message });
            SetStatus($"Error saving: {ex.Message}");
        }
    }

    private async Task LoginRowAsync(GoogleAccountRowViewModel? row)
    {
        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        if (row == null || !row.HasCredentials)
        {
            SetStatus("No credentials to login with");
            return;
        }

        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked. Please unlock the vault first.");
            return;
        }

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        // Find matching profile: by stable Id first, then by display name for
        // legacy rows that predate Id resolution.
        var profile = ResolveRowProfile(row);
        if (profile == null)
        {
            SetStatus($"❌ {row.ProfileName}: Profile not found");
            return;
        }

        row.ProfileId = profile.Id;

        // Create credential from row, keyed by the stable profile Id
        var credential = new GoogleLoginCredential(
            row.ProfileId,
            row.Email,
            row.Password,
            string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

        SetStatus($"🚀 Logging in {row.ProfileName}...");

        using var trace = TraceScope.Begin("CredentialsManager", "LoginGoogle", new { profile = row.ProfileName, has_totp = !string.IsNullOrWhiteSpace(row.TotpSecret) });

        try
        {
            var result = await _runGoogleAuthentication(profile, credential, CancellationToken.None);

            ObservabilityHub.Instance.IncrementCounter("credentials.login.google",
                tags: new Dictionary<string, string> { ["result"] = result.Category.ToString() });

            if (result.Category == GoogleLoginResultCategory.Success)
            {
                row.UpdateHealthStatus(CredentialHealthCheckResult.Healthy("Login successful"));
                SetStatus($"✓ {row.ProfileName}: Login successful");
            }
            else if (result.Category == GoogleLoginResultCategory.ManualInterventionRequired)
            {
                SetStatus($"⚠ {row.ProfileName}: Manual intervention required");
            }
            else
            {
                SetStatus($"❌ {row.ProfileName}: {result.Message ?? result.Category.ToString()}");
            }
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.IncrementCounter("credentials.login.google.exception",
                tags: new Dictionary<string, string> { ["error_type"] = ex.GetType().Name });
            ObservabilityHub.Instance.LogEvent(LogLevel.Error, "CredentialsManager", "GoogleLoginFailed",
                "Google login failed with exception", new { profile = row.ProfileName, error = ex.Message });
            SetStatus($"❌ {row.ProfileName}: {ex.Message}");
        }
    }

    private async Task CheckHealthRowAsync(GoogleAccountRowViewModel? row)
    {
        if (row == null || !row.HasCredentials)
        {
            SetStatus("No credentials to check");
            return;
        }

        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked. Please unlock the vault first.");
            return;
        }

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        var profile = ResolveRowProfile(row);
        if (profile == null)
        {
            row.UpdateHealthStatus(CredentialHealthCheckResult.Error($"Profile not found"));
            SetStatus($"❌ {row.ProfileName}: Profile not found");
            return;
        }

        row.ProfileId = profile.Id;

        var credential = new GoogleLoginCredential(
            row.ProfileId,
            row.Email,
            row.Password,
            string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

        row.UpdateHealthStatus(CredentialHealthCheckResult.Checking());
        SetStatus($"⟳ Checking health for {row.ProfileName}...");

        using var trace = TraceScope.Begin("CredentialsManager", "CheckHealth", new { profile = row.ProfileName });

        try
        {
            var result = await _runGoogleHealthCheck(profile, credential, CancellationToken.None);

            var healthResult = MapLoginResultToHealthCheck(result);
            row.UpdateHealthStatus(healthResult);

            ObservabilityHub.Instance.IncrementCounter("credentials.health_check.completed",
                tags: new Dictionary<string, string> { ["status"] = healthResult.Status.ToString() });

            SetStatus($"{healthResult.Status.ToEmoji()} {row.ProfileName}: {healthResult.Message}");
        }
        catch (Exception ex)
        {
            var healthResult = CredentialHealthCheckResult.Error($"Health check failed: {ex.Message}", ex);
            row.UpdateHealthStatus(healthResult);

            ObservabilityHub.Instance.IncrementCounter("credentials.health_check.exception",
                tags: new Dictionary<string, string> { ["error_type"] = ex.GetType().Name });
            ObservabilityHub.Instance.LogEvent(LogLevel.Error, "CredentialsManager", "HealthCheckFailed",
                "Health check failed with exception", new { profile = row.ProfileName, error = ex.Message });

            SetStatus($"❌ {row.ProfileName}: {ex.Message}");
        }
    }

    private async Task CheckAllHealthAsync()
    {
        var accountsWithCredentials = GoogleAccounts.Where(a => a.HasCredentials).ToList();
        if (!accountsWithCredentials.Any())
        {
            SetStatus("No configured accounts to check");
            return;
        }

        SetStatus($"Checking health for {accountsWithCredentials.Count} account(s)...");

        var healthyCount = 0;
        var unhealthyCount = 0;

        foreach (var account in accountsWithCredentials)
        {
            await CheckHealthRowAsync(account);

            if (account.HealthStatus?.Status.IsHealthy() == true)
            {
                healthyCount++;
            }
            else if (account.HealthStatus?.Status.NeedsAttention() == true)
            {
                unhealthyCount++;
            }

            // Small delay between checks to avoid overwhelming the system
            await Task.Delay(500);
        }

        SetStatus($"Health check completed: {healthyCount} healthy, {unhealthyCount} need attention");
    }

    private static CredentialHealthCheckResult MapLoginResultToHealthCheck(GoogleLoginResult loginResult)
    {
        return loginResult.Category switch
        {
            GoogleLoginResultCategory.Success =>
                CredentialHealthCheckResult.Healthy("Credentials are valid"),

            GoogleLoginResultCategory.InvalidCredentials =>
                CredentialHealthCheckResult.Invalid("Invalid email, password, or TOTP code"),

            GoogleLoginResultCategory.ManualInterventionRequired =>
                CredentialHealthCheckResult.RequiresAction(loginResult.Message),

            GoogleLoginResultCategory.Timeout =>
                CredentialHealthCheckResult.Error("Health check timed out"),

            GoogleLoginResultCategory.Cancelled =>
                CredentialHealthCheckResult.Error("Health check was cancelled"),

            GoogleLoginResultCategory.BrowserDisconnected =>
                CredentialHealthCheckResult.Error("Browser connection lost"),

            GoogleLoginResultCategory.UnsupportedPage =>
                CredentialHealthCheckResult.RequiresAction(loginResult.Message),

            _ => CredentialHealthCheckResult.Unknown($"Unexpected result: {loginResult.Category}")
        };
    }

    private Task StartBatchLoginAsync()
    {
        var selectedGoogleRows = GoogleAccounts
            .Where(a => a.IsSelected && a.HasCredentials)
            .ToList();
        var selectedCodexRows = CodexConnections
            .Where(c => c.IsSelected && c.HasCredentials)
            .ToList();
        var batchCts = new CancellationTokenSource();
        _batchLoginCts = batchCts;
        IsBatchLoginRunning = true;
        _batchLoginTask = BatchLoginAsync(batchCts, selectedGoogleRows, selectedCodexRows);
        OnPropertyChanged(nameof(BatchLoginTask));
        return _batchLoginTask;
    }

    private void StopBatchLogin()
    {
        _batchLoginCts?.Cancel();
    }

    private void ToggleSelectAllGoogle()
    {
        var credentialed = GoogleAccounts.Where(a => a.HasCredentials).ToList();
        if (!credentialed.Any()) return;

        var shouldSelect = !IsAllGoogleSelected;
        foreach (var account in credentialed)
        {
            account.IsSelected = shouldSelect;
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsAllGoogleSelected));
        OnPropertyChanged(nameof(IsGoogleSelectionIndeterminate));
        BatchLoginCommand.RaiseCanExecuteChanged();
    }

    private async Task BatchLoginAsync(
        CancellationTokenSource batchCts,
        IReadOnlyList<GoogleAccountRowViewModel> selectedGoogleRows,
        IReadOnlyList<CodexConnectionRowViewModel> selectedCodexRows)
    {
        // Yield so StartBatchLoginAsync can publish the task before preflight
        // exits through the cleanup path.
        await Task.Yield();

        var successCount = 0;
        var failCount = 0;
        var cancelled = false;

        var totalCount = selectedGoogleRows.Count + selectedCodexRows.Count;
        using var trace = TraceScope.Begin("CredentialsManager", "BatchLogin", new { profile_count = totalCount });

        try
        {
            if (totalCount == 0)
            {
                SetStatus("No profiles selected");
                return;
            }

            if (_vaultSession == null)
            {
                SetStatus("Vault not unlocked. Please unlock the vault first.");
                return;
            }

            ObservabilityHub.Instance.LogEvent(LogLevel.Info, "CredentialsManager", "BatchLoginStarted",
                "Starting batch login operation", new {
                    google_count = selectedGoogleRows.Count,
                    codex_count = selectedCodexRows.Count,
                    total_count = totalCount
                });

            SetStatus($"Starting batch login for {totalCount} profile(s) ({selectedGoogleRows.Count} Google, {selectedCodexRows.Count} Codex)...");

            // Process Google accounts first
            foreach (var row in selectedGoogleRows)
            {
                // Find matching profile: by stable Id first, then by display name
                // for legacy rows that predate Id resolution.
                batchCts.Token.ThrowIfCancellationRequested();

                var profile = ResolveRowProfile(row);
                if (profile == null)
                {
                    SetStatus($"❌ {row.ProfileName}: Profile not found");
                    failCount++;
                    continue;
                }

                row.ProfileId = profile.Id;

                try
                {
                    // Create credential from row, keyed by the stable profile Id.
                    // Invalid row data must fail only this profile, not the batch.
                    var credential = new GoogleLoginCredential(
                        row.ProfileId,
                        row.Email,
                        row.Password,
                        string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

                    SetStatus($"🚀 Logging in Google {row.ProfileName}...");

                    var result = await _runGoogleAuthentication(profile, credential, batchCts.Token);

                    if (batchCts.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    if (result.Category == GoogleLoginResultCategory.Success)
                    {
                        SetStatus($"✓ {row.ProfileName}: Google login successful");
                        successCount++;
                    }
                    else if (result.Category == GoogleLoginResultCategory.Cancelled)
                    {
                        SetStatus($"⏹ {row.ProfileName}: Google login cancelled");
                        cancelled = true;
                        break;
                    }
                    else if (result.Category == GoogleLoginResultCategory.ManualInterventionRequired)
                    {
                        SetStatus($"⚠ {row.ProfileName}: Google manual intervention required");
                        failCount++;
                    }
                    else
                    {
                        SetStatus($"❌ {row.ProfileName}: Google {result.Message}");
                        failCount++;
                    }
                }
                catch (OperationCanceledException) when (batchCts.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    SetStatus($"❌ {row.ProfileName}: Google {ex.Message}");
                    failCount++;
                }
            }

            // Process Codex accounts if not cancelled
            if (!cancelled)
            {
                foreach (var row in selectedCodexRows)
                {
                    batchCts.Token.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(row.ProfileId))
                    {
                        SetStatus($"❌ Codex {row.ProfileName}: Profile ID not resolved");
                        failCount++;
                        continue;
                    }

                    var profile = _mainViewModel.Profiles.FirstOrDefault(p => p.Id == row.ProfileId);
                    if (profile == null)
                    {
                        SetStatus($"❌ Codex {row.ProfileName}: Profile not found");
                        failCount++;
                        continue;
                    }

                    try
                    {
                        CodexLoginCredential credential;

                        if (row.AuthMethod == AuthMethod.GoogleOAuth)
                        {
                            // Google OAuth flow
                            if (string.IsNullOrWhiteSpace(row.LinkedGoogleAccount))
                            {
                                SetStatus($"❌ Codex {row.ProfileName}: No linked Google account");
                                failCount++;
                                continue;
                            }

                            var googleAccount = GoogleAccounts.FirstOrDefault(a =>
                                a.Email.Equals(row.LinkedGoogleAccount, StringComparison.OrdinalIgnoreCase));

                            if (googleAccount == null)
                            {
                                SetStatus($"❌ Codex {row.ProfileName}: Google account '{row.LinkedGoogleAccount}' not found");
                                failCount++;
                                continue;
                            }

                            // Validate that Google account is logged in
                            if (googleAccount.HealthStatus?.Status != CredentialHealthStatus.Healthy)
                            {
                                SetStatus($"❌ Codex {row.ProfileName}: Google account '{row.LinkedGoogleAccount}' must be logged in first");
                                failCount++;
                                continue;
                            }

                            credential = !string.IsNullOrWhiteSpace(row.TotpSecret)
                                ? CodexLoginCredential.FromGoogleOAuthWithTotp(row.ProfileId, row.LinkedGoogleAccount, row.TotpSecret.Trim())
                                : CodexLoginCredential.FromGoogleOAuth(row.ProfileId, row.LinkedGoogleAccount);
                        }
                        else // Direct method
                        {
                            if (string.IsNullOrWhiteSpace(row.Email) || string.IsNullOrWhiteSpace(row.Password))
                            {
                                SetStatus($"❌ Codex {row.ProfileName}: Email and password required for Direct login");
                                failCount++;
                                continue;
                            }

                            credential = CodexLoginCredential.FromDirect(
                                row.ProfileId,
                                row.Email,
                                row.Password,
                                string.IsNullOrWhiteSpace(row.TotpSecret) ? null : row.TotpSecret.Trim());
                        }

                        SetStatus($"🚀 Logging in Codex {row.ProfileName}...");

                        var result = await _runCodexAuthentication(profile, credential, batchCts.Token);

                        if (batchCts.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }

                        if (result.Category == CodexLoginResultCategory.Success)
                        {
                            SetStatus($"✓ Codex {row.ProfileName}: Login successful");
                            successCount++;
                        }
                        else if (result.Category == CodexLoginResultCategory.Cancelled)
                        {
                            SetStatus($"⏹ Codex {row.ProfileName}: Login cancelled");
                            cancelled = true;
                            break;
                        }
                        else if (result.Category == CodexLoginResultCategory.ManualInterventionRequired)
                        {
                            SetStatus($"⚠ Codex {row.ProfileName}: Manual intervention required");
                            failCount++;
                        }
                        else
                        {
                            SetStatus($"❌ Codex {row.ProfileName}: {result.Message}");
                            failCount++;
                        }
                    }
                    catch (OperationCanceledException) when (batchCts.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"❌ Codex {row.ProfileName}: {ex.Message}");
                        failCount++;
                    }
                }
            }

            ObservabilityHub.Instance.IncrementCounter("credentials.batch_login.completed");
            ObservabilityHub.Instance.RecordGauge("credentials.batch_login.success_count", successCount);
            ObservabilityHub.Instance.RecordGauge("credentials.batch_login.fail_count", failCount);

            SetStatus(cancelled
                ? $"Batch login cancelled: {successCount} succeeded, {failCount} failed"
                : $"Batch login completed: {successCount} succeeded, {failCount} failed");
        }
        catch (OperationCanceledException) when (batchCts.IsCancellationRequested)
        {
            ObservabilityHub.Instance.IncrementCounter("credentials.batch_login.cancelled");
            SetStatus($"Batch login cancelled: {successCount} succeeded, {failCount} failed");
        }
        finally
        {
            if (ReferenceEquals(_batchLoginCts, batchCts))
            {
                _batchLoginCts = null;
            }

            batchCts.Dispose();
            IsBatchLoginRunning = false;
            _batchLoginTask = null;
            OnPropertyChanged(nameof(BatchLoginTask));
        }
    }

    private Task RemoveGoogleAccountAsync()
    {
        return SelectedGoogleAccount is { } row
            ? RemoveGoogleAccountAsync(row)
            : Task.CompletedTask;
    }

    public async Task RemoveGoogleAccountAsync(GoogleAccountRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked");
            return;
        }

        var profileName = row.ProfileName;

        try
        {
            // Remove from vault (immutable - filter and replace). Key by the
            // stable profile Id, and ALSO drop a legacy name-keyed record only
            // when the display name maps unambiguously to this profile.
            var currentVault = _vaultSession.Vault;
            var filteredRecords = currentVault.Records
                .Where(r => r.ProfileId != row.ProfileId)
                .Where(r => !HasUniqueProfileName(row.ProfileName) || r.ProfileId != row.ProfileName);
            var newVault = new GoogleAccountVault(filteredRecords);
            _vaultSession.Replace(newVault);
            await _googleAccountVaultStore.SaveAsync(_vaultSession, CancellationToken.None);

            // Update UI - clear credentials but keep row.
            row.Email = string.Empty;
            row.Password = string.Empty;
            row.TotpSecret = string.Empty;
            row.HasCredentials = false;
            row.IsEditing = false;
            OnPropertyChanged(nameof(CanRemoveGoogleAccount));
            OnPropertyChanged(nameof(ConfiguredGoogleAccounts));
            RemoveGoogleAccountCommand.RaiseCanExecuteChanged();

            SetStatus($"Removed credentials for {profileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error removing account: {ex.Message}");
        }
    }

    public async Task RemoveGoogleAccountAsync(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            SetStatus("Profile is required");
            return;
        }

        var matchingRows = GoogleAccounts
            .Where(account => account.ProfileName == profileName)
            .ToList();
        if (matchingRows.Count != 1)
        {
            SetStatus(matchingRows.Count == 0
                ? $"Profile not found for {profileName}"
                : $"Profile name is ambiguous: {profileName}");
            return;
        }

        await RemoveGoogleAccountAsync(matchingRows[0]);
    }

}
