using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Core.Updates;
using RouterPlus.App;
using RouterPlus.App.Views;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Services;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Infrastructure.Updates;
using RouterPlus.App.Services;

namespace RouterPlus.App.ViewModels;

public sealed partial class MainViewModel
{
    /// Used to filter profiles eligible for batch auto-login.
    /// </summary>
    public async Task<bool> HasVaultCredentialsAsync(
        ChromeProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (profile is null)
        {
            return false;
        }

        try
        {
            // Check all providers - returns true if any provider has credentials
            foreach (ProviderKind kind in Enum.GetValues(typeof(ProviderKind)))
            {
                if (kind == ProviderKind.Ollama || kind == ProviderKind.Agnes)
                {
                    // Skip providers without auto-login support
                    continue;
                }

                var hasCreds = await _providerConnectionVaultStore.HasCredentialsAsync(
                    profile.Name,
                    kind,
                    cancellationToken);

                if (hasCreds)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogError(
                "ViewModel",
                "VaultCredentialsCheckFailed",
                ex,
                new { profile_name = profile.Name });
            return false;
        }
    }

    /// <summary>
    /// Batch Phase 2: Select only profiles that have vault credentials configured.
    /// Useful for quick batch auto-login setup.
    /// </summary>
    public async Task SelectProfilesWithVaultCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isMultiSelectMode)
        {
            IsMultiSelectMode = true;
        }

        int selectedCount = 0;
        foreach (var row in ProfileRows)
        {
            var hasCreds = await HasVaultCredentialsAsync(row.Profile, cancellationToken);
            row.IsSelected = hasCreds;
            if (hasCreds)
            {
                selectedCount++;
            }
        }

        OnPropertyChanged(nameof(HasSelectedProfiles));
        OnPropertyChanged(nameof(SelectedProfilesText));
        StatusText = selectedCount > 0
            ? $"Đã chọn {selectedCount} profile có vault credentials"
            : "Không có profile nào có vault credentials";
    }

    /// <summary>
    /// Batch Phase 4: Start batch auto-login for all selected profiles.
    /// Sequential execution with auto-skip, continue-on-failure, and 2s delays.
    /// </summary>
    private async Task StartBatchAutoLoginAsync()
    {
        var profiles = SelectedProfileRows.Select(r => r.Profile).ToArray();
        if (profiles.Length == 0)
        {
            return;
        }

        // Setup
        IsBatchLoginRunning = true;
        BatchProgressRows.Clear();
        CloseBatchProgressCommand.RaiseCanExecuteChanged();
        _batchLoginCts = new CancellationTokenSource();
        var ct = _batchLoginCts.Token;

        try
        {
            foreach (var profile in profiles)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var row = new BatchLoginProgressRow(profile)
                {
                    State = BatchLoginState.InProgress,
                    StatusMessage = "Đang kiểm tra vault..."
                };
                BatchProgressRows.Add(row);
                OnPropertyChanged(nameof(BatchProgressSummary));

                var sw = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    // Check vault credentials first
                    var hasCreds = await HasVaultCredentialsAsync(profile, ct);
                    if (!hasCreds)
                    {
                        row.State = BatchLoginState.Skipped;
                        row.StatusMessage = "Không có vault credentials";
                        row.Duration = sw.Elapsed;
                        OnPropertyChanged(nameof(BatchProgressSummary));
                        continue;
                    }

                    row.StatusMessage = "Đang đăng nhập...";

                    // Try login with each provider that has credentials
                    var anySuccess = await TryLoginProfileAllProvidersAsync(profile, row, ct);
                    row.Duration = sw.Elapsed;

                    if (anySuccess)
                    {
                        row.State = BatchLoginState.Success;
                    }
                    else if (row.State == BatchLoginState.InProgress)
                    {
                        row.State = BatchLoginState.Failed;
                        if (string.IsNullOrEmpty(row.StatusMessage) || row.StatusMessage == "Đang đăng nhập...")
                        {
                            row.StatusMessage = "Không thể đăng nhập";
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    row.State = BatchLoginState.Failed;
                    row.StatusMessage = "Đã hủy";
                    row.Duration = sw.Elapsed;
                    throw;
                }
                catch (Exception ex)
                {
                    row.State = BatchLoginState.Failed;
                    row.StatusMessage = ex.Message;
                    row.Duration = sw.Elapsed;
                    ObservabilityHub.Instance.LogError(
                        "ViewModel",
                        "BatchLoginFailed",
                        ex,
                        new { profile_name = profile.Name });
                }
                finally
                {
                    OnPropertyChanged(nameof(BatchProgressSummary));
                }

                // 2s delay between profiles (skip if cancelled)
                if (!ct.IsCancellationRequested && profile != profiles[^1])
                {
                    await Task.Delay(2000, ct);
                }
            }

            StatusText = ct.IsCancellationRequested
                ? "Đã hủy batch auto-login"
                : $"Hoàn thành batch: {BatchProgressSummary}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Batch auto-login đã bị hủy";
        }
        catch (Exception ex)
        {
            StatusText = $"Lỗi batch: {ex.Message}";
            ObservabilityHub.Instance.LogError(
                "ViewModel",
                "BatchAutoLoginFailed",
                ex,
                new { });
        }
        finally
        {
            IsBatchLoginRunning = false;
            _batchLoginCts?.Dispose();
            _batchLoginCts = null;
            StartBatchAutoLoginCommand.RaiseCanExecuteChanged();
            StopBatchLoginCommand.RaiseCanExecuteChanged();
            CloseBatchProgressCommand.RaiseCanExecuteChanged();
        }
    }

    private void StopBatchLogin()
    {
        _batchLoginCts?.Cancel();
    }

    private void CloseBatchProgress()
    {
        // Exit multi-select mode after batch completes
        IsMultiSelectMode = false;
        BatchProgressRows.Clear();
        OnPropertyChanged(nameof(BatchProgressSummary));
    }

    /// <summary>
    /// Try auto-login for a profile across all providers that have credentials.
    /// Returns true if any provider login succeeds.
    /// </summary>
    private async Task<bool> TryLoginProfileAllProvidersAsync(
        ChromeProfile profile,
        BatchLoginProgressRow row,
        CancellationToken ct)
    {
        if (_installation is null)
        {
            row.StatusMessage = "Chrome chưa được cấu hình";
            return false;
        }

        bool anySuccess = false;

        foreach (ProviderKind kind in Enum.GetValues(typeof(ProviderKind)))
        {
            if (ct.IsCancellationRequested)
            {
                return anySuccess;
            }

            // Skip providers without auto-login support
            if (kind == ProviderKind.Ollama || kind == ProviderKind.Agnes)
            {
                continue;
            }

            bool hasCreds = false;
            try
            {
                hasCreds = await _providerConnectionVaultStore.HasCredentialsAsync(
                    profile.Name, kind, ct);
            }
            catch
            {
                continue;
            }

            if (!hasCreds)
            {
                continue;
            }

            row.StatusMessage = $"Đang đăng nhập {kind}...";

            try
            {
                var startUri = GetProviderLoginUri(kind);
                var result = await RunAutoLoginWithOrchestratorAsync(profile, kind, startUri, ct);

                if (result.Success)
                {
                    row.StatusMessage = $"Thành công ({kind})";
                    anySuccess = true;
                    // Stop trying other providers once one succeeds
                    break;
                }
                else
                {
                    row.StatusMessage = $"{kind}: {result.ErrorMessage}";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                row.StatusMessage = $"{kind}: {ex.Message}";
                ObservabilityHub.Instance.LogError(
                    "ViewModel",
                    "ProviderLoginFailed",
                    ex,
                    new { provider_kind = kind.ToString(), profile_name = profile.Name });
            }
        }

        return anySuccess;
    }

    /// <summary>
    /// Get the login URL for a provider.
    /// </summary>
    private static Uri GetProviderLoginUri(ProviderKind provider)
    {
        return ProviderCatalog.GetDirectLoginStartUri(provider) ?? new Uri("https://chatgpt.com/");
    }

    private void Profiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanAddProfile));
        OnPropertyChanged(nameof(ProfileAddButtonText));
        AddProfileCommand.RaiseCanExecuteChanged();
        UpdateRecentProfilesList();
    }

    public Task RefreshConnectionStatusesAsync(CancellationToken cancellationToken = default) =>
        RefreshConnectionStatusesAsync(showStatus: true, forceLog: true, cancellationToken);

    public async Task<IReadOnlyList<ProviderConnection>> RefreshConnectionsLightweightAsync(
        CancellationToken cancellationToken = default)
    {
        var connections = await _connectionService.RefreshConnectionsLightweightAsync(
            async () => (IRouterApiClient)await CreateApiClientAsync(),
            cancellationToken);

        foreach (var row in ProfileRows)
        {
            row.UpdateConnections(connections);
        }

        OnPropertyChanged(nameof(SelectedProfileRow));
        UpdateProviderCardStatuses();
        UpdateProviderFilterCounts();
        ApplyProfileFilter();

        return connections;
    }

    public void StartQuotaPolling() => _quotaPollingService.Start();

    public void PauseQuotaPolling() => _quotaPollingService.Pause();

    public Task ResumeQuotaPollingAsync() => _quotaPollingService.ResumeAsync();

    public Task StopQuotaPollingAsync() => _quotaPollingService.StopAsync();

    public async Task DisposeGoogleLoginSessionsAsync()
    {
        foreach (var (session, browser) in _googleLoginSessions.ToArray())
        {
            try
            {
                await browser.DisposeAsync();
            }
            catch
            {
                // Best effort cleanup.
            }

            try
            {
                await session.DisposeAsync();
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        _googleLoginSessions.Clear();
    }

    private async Task<bool> RefreshForQuotaPollingAsync(CancellationToken cancellationToken)
    {
        await RefreshConnectionStatusesAsync(showStatus: false, forceLog: false, cancellationToken);
        return _lastReliableNearLimit;
    }

    private bool _lastReliableNearLimit;

    private async Task RefreshConnectionStatusesAsync(
        bool showStatus,
        bool forceLog = false,
        CancellationToken cancellationToken = default)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Providers",
            "ConnectionRefreshRequested",
            "Provider sync requested",
            new { profile_count = ProfileRows.Count, forceLog });

        // Coalesce duplicate background refreshes: skip non-forced if another refresh is running
        if (!forceLog)
        {
            if (!await _connectionRefreshGate.WaitAsync(0, cancellationToken))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "Providers",
                    "ConnectionRefreshCoalesced",
                    "Skipped duplicate background refresh (another refresh in progress)",
                    new { profile_count = ProfileRows.Count });
                return;
            }
        }
        else
        {
            await _connectionRefreshGate.WaitAsync(cancellationToken);
        }

        try
        {
            await RefreshConnectionStatusesCoreAsync(showStatus, forceLog, cancellationToken);
        }
        finally
        {
            _connectionRefreshGate.Release();
        }
    }

    private async Task LoadSelectedProfileApiKeysAsync()
    {
        var loadVersion = Interlocked.Increment(ref _apiKeyLoadVersion);
        var profile = SelectedProfile;
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "Security",
            "LoadingProfileCredentials",
            "Loading saved provider credentials for profile",
            new { has_profile = profile is not null });
        foreach (var card in ProviderCards.Where(card => card.Workflow == WorkflowKind.ApiKey))
        {
            card.LoadSavedApiKey(null);
        }

        if (profile is null)
        {
            return;
        }

        try
        {
            var values = await Task.WhenAll(ApiKeyProviders.Select(async definition =>
                new
                {
                    definition.Kind,
                    Value = await _secretVault.ReadAsync(ProfileSecretKey.Create(profile, definition.Kind))
                }));

            if (loadVersion != Volatile.Read(ref _apiKeyLoadVersion) || !Equals(SelectedProfile, profile))
            {
                return;
            }

            foreach (var value in values)
            {
                GetProviderCard(value.Kind).LoadSavedApiKey(value.Value);
            }

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Security",
                "ProfileCredentialsLoaded",
                "Saved provider credentials loaded",
                new { provider_count = values.Length });
        }
        catch (Exception exception)
        {
            ObservabilityHub.Instance.LogError(
                "Security",
                "ProfileCredentialsLoadFailed",
                exception,
                new { profile_name = profile.Name });
            if (loadVersion == Volatile.Read(ref _apiKeyLoadVersion))
            {
                AppendLog("WARN", $"Không thể đọc API key đã lưu: {SafeError(exception)}");
            }
        }
    }

    private async Task RefreshConnectionStatusesCoreAsync(
        bool showStatus,
        bool forceLog = false,
        CancellationToken cancellationToken = default)
    {
        if (ProfileRows.Count == 0)
        {
            _lastReliableNearLimit = false;
            UpdateProviderCardStatuses();
            ConnectionStatusText = "Chưa có Chrome profile để đối chiếu.";
            if (showStatus && forceLog)
            {
                SetStatusText(ConnectionStatusText, "SYNC", forceLog: true);
            }

            return;
        }

        try
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Providers",
                "LoadingConnections",
                "Loading provider connections",
                new { });

            await LoadQuotaAutoDisableMarkersAsync();

            var refreshResult = await _connectionService.RefreshConnectionsAsync(
                async () => (IRouterApiClient)await CreateApiClientAsync(),
                cancellationToken);
            var connections = refreshResult.Connections;
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Providers",
                "ConnectionsLoaded",
                "Provider connections loaded",
                new { connection_count = connections.Count });
            _lastReliableNearLimit = refreshResult.NearLimit;

            var disableFailures = refreshResult.DisableFailures.Count;
            foreach (var disabledConnection in refreshResult.DisabledConnections)
            {
                UpsertQuotaAutoDisableMarker(disabledConnection);
            }
            if (refreshResult.DisabledConnections.Count > 0)
            {
                await SaveQuotaAutoDisableMarkersAsync(cancellationToken);
            }

            foreach (var failure in refreshResult.DisableFailures)
            {
                AppendLog("WARN", $"Không thể tự tắt connection {failure.ConnectionId}: {SafeError(failure.Exception)}");
            }

            if (refreshResult.FollowUpRefreshException is not null)
            {
                throw refreshResult.FollowUpRefreshException;
            }

            await UpdateQuotaResetSuggestionsAsync(connections, cancellationToken);

            var allProfilesList = ProfileRows.Select(r => r.Profile).ToArray();
            foreach (var row in ProfileRows)
            {
                row.UpdateConnections(connections, allProfilesList);
            }

            OnPropertyChanged(nameof(SelectedProfileRow));
            UpdateProviderCardStatuses();
            UpdateProviderFilterCounts();
            ApplyProfileFilter();

            var matchedProfiles = ProfileRows.Count(row => row.ConnectedProviderCount > 0);
            var lostRows = ProfileRows.Where(row => row.ProviderStatuses.Any(status => status.HasLostConnection)).ToList();

            // Track unmatched connections (not assigned to any profile/provider)
            var matchedConnectionIds = new HashSet<string>(ProfileRows
                .SelectMany(row => row.ProviderStatuses.SelectMany(status => status.Connections))
                .Where(c => !string.IsNullOrEmpty(c.Id))
                .Select(c => c.Id!), StringComparer.Ordinal);
            var unmatchedCount = connections.Count(c => !string.IsNullOrEmpty(c.Id) && !matchedConnectionIds.Contains(c.Id!));

            ConnectionStatusText = disableFailures > 0
                ? $"Đã đồng bộ {connections.Count} connection · tắt tự động thất bại {disableFailures} connection."
                : $"Đã đồng bộ {connections.Count} connection · {matchedProfiles}/{ProfileRows.Count} profile có provider.";
            if (lostRows.Count > 0)
            {
                var lostNames = string.Join(", ", lostRows.Select(row => row.Name));
                ConnectionStatusText += $" · Mất connection: {lostNames} — thêm lại";
                AppendLog("WARN", $"Mất connection: {lostNames} — thêm lại");
            }
            if (unmatchedCount > 0)
            {
                ConnectionStatusText += $" · {unmatchedCount} connection chưa khớp profile";
            }
            if (showStatus)
            {
                if (forceLog)
                {
                    SetStatusText(ConnectionStatusText, "SYNC", forceLog: true);
                }
                else
                {
                    StatusText = ConnectionStatusText;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _lastReliableNearLimit = false;
            foreach (var row in ProfileRows)
            {
                row.MarkStatusUnknown();
            }
            UpdateProviderCardStatuses();
            ApplyProfileFilter();

            ConnectionStatusText = $"Chưa đồng bộ provider: {SafeError(exception)}";
            if (showStatus)
            {
                if (forceLog)
                {
                    SetStatusText(ConnectionStatusText, "SYNC", forceLog: true);
                }
                else
                {
                    StatusText = ConnectionStatusText;
                }
            }
            else
            {
                AppendLog("WARN", ConnectionStatusText);
            }
        }
    }

}