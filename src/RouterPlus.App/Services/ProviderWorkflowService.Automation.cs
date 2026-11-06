using System.Net.Http;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.App.Services;

public sealed partial class ProviderWorkflowService
{

    public async Task RunOAuthWorkflowAsync(IRouterApiClient api, ProviderKind provider, CancellationToken cancellationToken)
    {
        WorkflowStartedAt = DateTimeOffset.UtcNow;
        await CaptureExistingConnectionsAsync(api, provider, cancellationToken);
        var definition = ProviderCatalog.Get(provider);
        if (provider == ProviderKind.Codex)
        {
            var session = await api.StartOAuthAuthorizationAsync(provider, "http://localhost:1455/auth/callback", cancellationToken);
            OAuthProxyStartResult proxy;
            try
            {
                proxy = await api.StartOAuthProxyAsync(provider, _getDashboardPort(), session, cancellationToken);
            }
            catch (RouterApiException)
            {
                CurrentProvider = null;
                await _launchUrlAsync(definition.BuildDashboardUrl(_getDashboardBaseUrl()));
                _setStatus("9Router không bật được OAuth proxy tự động. Trong dashboard, bấm Thêm Codex rồi quay lại bấm Chờ connection thủ công.");
                return;
            }

            if (!proxy.ServerSide)
            {
                CurrentProvider = null;
                await _launchUrlAsync(definition.BuildDashboardUrl(_getDashboardBaseUrl()));
                _setStatus("OAuth proxy chưa sẵn sàng. Trong dashboard, bấm Thêm Codex rồi quay lại bấm Chờ connection thủ công.");
                return;
            }

            _setStatus("Đang mở đăng nhập Codex và tự động xác nhận OAuth…");
            await RunOAuthAutoLoginAsync(provider, session.AuthUrl, new Uri("https://chatgpt.com"), cancellationToken);
            await WaitForOAuthProxyAsync(api, provider, session.State, TimeSpan.FromMinutes(10), cancellationToken);
        }
        else
        {
            await using var callbackListener = await OAuthCallbackListener.StartAsync();
            var session = await api.StartOAuthAuthorizationAsync(provider, callbackListener.RedirectUri.ToString(), cancellationToken);
            _setStatus($"Đang mở đăng nhập {definition.DisplayName} và tự động xác nhận OAuth…");
            var targetUri = definition.QuickLink is { } ql ? new Uri(ql) : null;
            if (targetUri is not null)
            {
                await RunOAuthAutoLoginAsync(provider, session.AuthUrl, targetUri, cancellationToken);
            }
            var callback = await callbackListener.WaitForCallbackAsync(TimeSpan.FromMinutes(10), cancellationToken);
            if (!string.IsNullOrWhiteSpace(callback.Error))
            {
                throw new InvalidOperationException(callback.ErrorDescription ?? callback.Error);
            }
            if (string.IsNullOrWhiteSpace(callback.Value))
            {
                throw new InvalidOperationException("OAuth callback không có code hoặc token.");
            }
            if (!callback.MatchesState(session.State))
            {
                throw new InvalidOperationException("OAuth callback state không khớp phiên đăng nhập.");
            }
            await api.ExchangeOAuthCodeAsync(provider, callback.Value, session.RedirectUri, session.CodeVerifier, callback.State, cancellationToken);
        }

        await RenameNewConnectionAsync(api, provider, cancellationToken);
    }

    public async Task RunDeviceCodeWorkflowAsync(IRouterApiClient api, ProviderKind provider, CancellationToken cancellationToken)
    {
        WorkflowStartedAt = DateTimeOffset.UtcNow;
        await CaptureExistingConnectionsAsync(api, provider, cancellationToken);
        var definition = ProviderCatalog.Get(provider);
        var session = await api.StartDeviceCodeAsync(provider, provider == ProviderKind.Kiro ? "idc" : null, cancellationToken);

        if (provider is ProviderKind.Kiro or ProviderKind.Nous)
        {
            _setStatus($"Đang mở Chrome và tự động xác nhận {definition.DisplayName}…");
            await RunDeviceCodeWithAutomationAsync(api, provider, session.VerificationUriComplete ?? session.VerificationUri, session, cancellationToken);
        }
        else
        {
            await _launchUrlAsync(session.VerificationUriComplete ?? session.VerificationUri);
            _setStatus($"Đã mở trang xác nhận cho {definition.DisplayName}. Hoàn tất xác nhận; tool đang tự chờ connection…");

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Max(session.ExpiresIn, 60));
            var intervalSeconds = Math.Clamp(session.Interval, 1, 30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var result = await api.PollDeviceCodeAsync(provider, session, cancellationToken);
                if (result.Success)
                {
                    await RenameNewConnectionAsync(api, provider, cancellationToken);
                    return;
                }

                if (!string.Equals(result.Error, "authorization_pending", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(result.Error, "slow_down", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(result.ErrorDescription ?? result.Error ?? "Device-code authorization failed.");
                }

                if (string.Equals(result.Error, "slow_down", StringComparison.OrdinalIgnoreCase))
                {
                    intervalSeconds = Math.Min(intervalSeconds + 5, 30);
                }

                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
            }

            throw new TimeoutException($"Hết thời gian chờ đăng nhập {definition.DisplayName}.");
        }
    }

    public async Task RunDeviceCodeWithAutomationAsync(IRouterApiClient api, ProviderKind provider, string verificationUri, DeviceCodeSession session, CancellationToken cancellationToken)
    {
        var selectedProfile = _selectedProfileGetter();
        var installation = _installationGetter();
        if (selectedProfile is null || installation is null)
        {
            await _launchUrlAsync(verificationUri);
            _setStatus("Không thể tự động hóa. Vui lòng hoàn tất thủ công.");
            await PollDeviceCodeUntilSuccessAsync(api, provider, session, cancellationToken);
            return;
        }

        var googleResult = await _tryAutoGoogleLogin(selectedProfile, cancellationToken);
        if (googleResult == GooglePreLoginResult.ManualInterventionRequired)
        {
            _setStatus("Cần hoàn tất xác minh Google. Vui lòng hoàn tất đăng nhập Google trong Chrome rồi thử lại.");
            return;
        }

        ChromeManagedSession? chromeSession = null;
        var definition = ProviderCatalog.Get(provider);
        try
        {
            ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Providers", "DeviceCodeAutomationStarted", "Device code automation start", new { profile_name = selectedProfile.Name });

            chromeSession = await _launchManagedChrome(installation, selectedProfile, new Uri(verificationUri), cancellationToken);

            _setStatus($"Đã mở Chrome với profile {selectedProfile.Name}. Đang tự động xác nhận…");

            var cdp = await chromeSession.ConnectAnyTargetAsync(cancellationToken);

            Func<Task<string?>>? totpGenerator = null;
            totpGenerator = await _getTotpGenerator(selectedProfile, cancellationToken);

            GoogleOAuthFlowAutomation automation = provider switch
            {
                ProviderKind.Kiro => new AwsBuilderIdOAuthAutomation(cdp.Client, cdp.SessionId, cdp.TargetId, selectedProfile.Name, totpGenerator),
                ProviderKind.Nous => new NousOAuthAutomation(cdp.Client, cdp.SessionId, cdp.TargetId, selectedProfile.Name, totpGenerator),
                _ => throw new NotSupportedException($"Automation not supported for provider {provider}")
            };

            var automationTask = automation.WaitAndConsentAsync(new Uri(verificationUri), TimeSpan.FromMinutes(10), cancellationToken);

            _setStatus($"Đang chờ xác nhận từ {definition.DisplayName}… Nếu Google yêu cầu TOTP/chọn tài khoản, hãy hoàn tất trong Chrome; app sẽ tự tiếp tục.");

            var pollTask = PollDeviceCodeUntilSuccessAsync(api, provider, session, cancellationToken);

            var completed = await Task.WhenAny(automationTask, pollTask);
            if (completed == automationTask)
            {
                var result = await automationTask;
                if (!result.Success)
                {
                    _setStatus($"Không thể tự động chọn tài khoản Google. Hãy chọn/đăng nhập tài khoản trong Chrome; app vẫn đang chờ {definition.DisplayName}…");
                    ObservabilityHub.Instance.LogEvent(LogLevel.Warning, "Providers", "DeviceCodeAutomationIncomplete", "Automation incomplete, continuing to wait for poll", new { message = result.Message });
                }
            }
            else
            {
                _ = automationTask.ContinueWith(
                    t =>
                    {
                        try
                        {
                            if (t.Exception is not null)
                            {
                                ObservabilityHub.Instance.LogError("Providers", "DeviceCodeAutomationFailed", t.Exception.InnerException ?? t.Exception, new { });
                            }
                        }
                        catch
                        {
                            // Never let continuation throw.
                        }
                    },
                    TaskContinuationOptions.OnlyOnFaulted);
            }

            await pollTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogError("Providers", "DeviceCodeAutomationFailed", ex, new { });
            _setStatus($"Tự động hóa lỗi: {ex.Message}. Đang chờ hoàn tất thủ công…");
            await PollDeviceCodeUntilSuccessAsync(api, provider, session, cancellationToken);
        }
        finally
        {
            if (chromeSession is not null)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                    await chromeSession.DisposeAsync();
                }
                catch { }
            }
        }
    }

    public async Task PollDeviceCodeUntilSuccessAsync(IRouterApiClient api, ProviderKind provider, DeviceCodeSession session, CancellationToken cancellationToken)
    {
        var definition = ProviderCatalog.Get(provider);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Max(session.ExpiresIn, 60));
        var intervalSeconds = Math.Clamp(session.Interval, 1, 30);
        int consecutiveTransientFailures = 0;
        const int maxConsecutiveTransientFailures = 10;

        while (DateTimeOffset.UtcNow < deadline)
        {
            DeviceCodePollResult result;
            try
            {
                result = await api.PollDeviceCodeAsync(provider, session, cancellationToken);
                consecutiveTransientFailures = 0;
            }
            catch (Exception ex) when (IsTransientPollException(ex, cancellationToken))
            {
                consecutiveTransientFailures++;
                if (consecutiveTransientFailures >= maxConsecutiveTransientFailures)
                {
                    throw;
                }
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
                continue;
            }

            if (result.Success)
            {
                await RenameNewConnectionAsync(api, provider, cancellationToken);
                return;
            }

            if (!string.Equals(result.Error, "authorization_pending", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(result.Error, "slow_down", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(result.ErrorDescription ?? result.Error ?? "Device-code authorization failed.");
            }

            if (string.Equals(result.Error, "slow_down", StringComparison.OrdinalIgnoreCase))
            {
                intervalSeconds = Math.Min(intervalSeconds + 5, 30);
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
        }

        throw new TimeoutException($"Hết thời gian chờ đăng nhập {definition.DisplayName}.");
    }

    private static bool IsTransientPollException(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is RouterApiException apiEx)
        {
            var status = (int)apiEx.StatusCode;
            return status == 408 || status == 429 || status >= 500;
        }
        if (ex is HttpRequestException) return true;
        if (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested) return true;
        return false;
    }

    public async Task CaptureExistingConnectionsAsync(IRouterApiClient api, ProviderKind provider, CancellationToken cancellationToken = default)
    {
        WorkflowStartedAt = DateTimeOffset.UtcNow;
        var existing = await api.ListConnectionsAsync(provider, cancellationToken, includeQuotas: false);
        WorkflowExistingConnections = existing.ToDictionary(connection => connection.Id, StringComparer.Ordinal);
        CurrentProvider = provider;
    }

    public async Task WaitForOAuthProxyAsync(IRouterApiClient api, ProviderKind provider, string state, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await api.GetOAuthProxyStatusAsync(provider, state, cancellationToken);
            if (string.Equals(status.Status, "done", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(status.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(status.Error ?? "OAuth authorization failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        throw new TimeoutException($"Hết thời gian chờ đăng nhập {ProviderCatalog.Get(provider).DisplayName}.");
    }

    public async Task RenameNewConnectionAsync(IRouterApiClient api, ProviderKind provider, CancellationToken cancellationToken = default)
    {
        var profile = _selectedProfileGetter() ?? throw new InvalidOperationException("Hãy chọn Chrome profile trước.");
        if (WorkflowStartedAt == default)
        {
            throw new InvalidOperationException("Workflow start time has not been captured.");
        }

        _setStatus($"Đã xác nhận {ProviderCatalog.Get(provider).DisplayName}. Đang chờ 9Router tạo connection (có thể mất 1-2 phút)…");

        var result = await _renameService.RenameAsync(
            api, provider, profile, WorkflowExistingConnections, WorkflowStartedAt, TimeSpan.FromMinutes(5), cancellationToken);

        var connection = result.Connection;
        if (result.FoundAfterTimeout)
        {
            ObservabilityHub.Instance.LogEvent(LogLevel.Info, "OAuthWorkflow", "ConnectionFoundAfterWaitTimeout", "Found matching connection after WaitForNewConnectionAsync timeout", new { provider = provider.ToString(), connectionId = connection.Id, profileName = profile.Name, reusedId = result.ReusedExistingId });
        }

        WorkflowExistingConnections[connection.Id] = connection;
        CurrentProvider = null;
        _setStatus($"Đã kết nối {ProviderCatalog.Get(provider).DisplayName} với profile {profile.Name}.");
        _showToast($"Đã kết nối {ProviderCatalog.Get(provider).DisplayName} với profile {profile.Name}.", ToastType.Success, 5);
        await _refreshConnectionsLightweight(cancellationToken);
    }

    public async Task WaitForConnectionAsync()
    {
        var currentProvider = CurrentProvider;
        var selectedProfile = _selectedProfileGetter();
        if (currentProvider is null || selectedProfile is null)
        {
            _setStatus("Chưa có workflow OAuth đang chờ.");
            _showToast("Chưa có workflow OAuth đang chờ.", ToastType.Warning, 5);
            return;
        }

        using var cancellation = new CancellationTokenSource();
        WorkflowCancellation = cancellation;
        IsInProgress = true;
        try
        {
            var cancellationToken = cancellation.Token;
            var provider = currentProvider.Value;
            var profile = selectedProfile ?? throw new InvalidOperationException("Hãy chọn Chrome profile trước.");
            if (WorkflowStartedAt == default)
            {
                throw new InvalidOperationException("Workflow start time has not been captured.");
            }
            var api = await _createApiClient();
            _setStatus($"Đang chờ {ProviderCatalog.Get(provider).DisplayName} báo connection mới…");
            var result = await _renameService.RenameAsync(api, provider, profile, WorkflowExistingConnections, WorkflowStartedAt, TimeSpan.FromMinutes(10), cancellationToken);
            var connection = result.Connection;
            if (result.FoundAfterTimeout)
            {
                ObservabilityHub.Instance.LogEvent(LogLevel.Info, "OAuthWorkflow", "ConnectionFoundAfterWaitTimeout", "Found matching connection after WaitForNewConnectionAsync timeout", new { provider = provider.ToString(), connectionId = connection.Id, profileName = profile.Name, reusedId = result.ReusedExistingId });
            }
            WorkflowExistingConnections[connection.Id] = connection;
            CurrentProvider = null;
            _setStatus($"Đã kết nối {ProviderCatalog.Get(provider).DisplayName} với profile {profile.Name}.");
            _showToast($"Đã kết nối {ProviderCatalog.Get(provider).DisplayName} với profile {profile.Name}.", ToastType.Success, 5);
            await _refreshConnectionsLightweight(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            CurrentProvider = null;
            WorkflowExistingConnections.Clear();
            _setStatus("Đã hủy thao tác chờ connection.");
            _showToast("Đã hủy thao tác chờ connection.", ToastType.Warning, 5);
        }
        catch (Exception exception)
        {
            _setError(exception);
        }
        finally
        {
            if (ReferenceEquals(WorkflowCancellation, cancellation))
            {
                WorkflowCancellation = null;
            }
            IsInProgress = false;
        }
    }

    public async Task RunOAuthAutoLoginAsync(ProviderKind provider, string authUrl, Uri targetServiceUri, CancellationToken cancellationToken)
    {
        var selectedProfile = _selectedProfileGetter();
        var installation = _installationGetter();
        if (selectedProfile is null || installation is null) return;

        var googleResult = await _tryAutoGoogleLogin(selectedProfile, cancellationToken);
        if (googleResult == GooglePreLoginResult.ManualInterventionRequired)
        {
            _setStatus("Cần hoàn tất xác minh Google. Vui lòng hoàn tất đăng nhập Google trong Chrome rồi thử lại.");
            return;
        }

        ChromeManagedSession? chromeSession = null;
        try
        {
            ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Providers", "OAuthAutoLoginStarted", "OAuth auto-login start", new { profile_name = selectedProfile.Name });

            chromeSession = await _launchManagedChrome(installation, selectedProfile, new Uri(authUrl), cancellationToken);

            _setStatus($"Đã mở Chrome với profile {selectedProfile.Name}. Đang chờ Google account picker…");

            var cdp = await chromeSession.ConnectAnyTargetAsync(cancellationToken);
            await using var orchestrator = new OAuthAutoLoginOrchestrator(chromeSession, cdp, provider);

            var result = await orchestrator.RunAsync(new Uri(authUrl), targetServiceUri, selectedProfile.Name, provider == ProviderKind.Codex ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(2), cancellationToken);

            switch (result.Outcome)
            {
                case OAuthAutoLoginOutcome.Success:
                    _setStatus(result.AlreadyAuthorized ? "Đã đăng nhập sẵn. Đang lưu connection…" : "Đã hoàn tất OAuth consent. Đang lưu connection…");
                    ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Providers", "OAuthAutoLoginSuccess", "OAuth auto-login success", new { message = result.Message });
                    break;
                default:
                    _setStatus("Auto-login chưa hoàn tất. Vui lòng click cho phép trong cửa sổ Chrome…");
                    ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Providers", "OAuthAutoLoginFallback", "OAuth auto-login fallback", new { message = result.Message });
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogError("Providers", "OAuthAutoLoginFailed", ex, new { });
            _setStatus($"Auto-login lỗi: {ex.Message}. Vui lòng hoàn tất thủ công.");
        }
        finally
        {
            if (chromeSession is not null)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                    await chromeSession.DisposeAsync();
                }
                catch { }
            }
        }
    }
}