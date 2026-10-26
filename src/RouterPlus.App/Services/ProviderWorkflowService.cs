using System.Net.Http;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.App.Services;

public sealed class ProviderWorkflowService : IProviderWorkflowService
{
    private readonly IProviderConnectionRenameService _renameService;
    private readonly IProviderApiKeyService _apiKeyService;
    private readonly Func<ChromeProfile?> _selectedProfileGetter;
    private readonly Func<ChromeInstallation?> _installationGetter;
    private readonly Func<ChromeInstallation, ChromeProfile, Uri, CancellationToken, Task<ChromeManagedSession>> _launchManagedChrome;
    private readonly Func<ChromeProfile, string, Task> _launchUrl;
    private readonly Func<string, Task> _launchUrlAsync;
    private readonly Func<Task<IRouterApiClient>> _createApiClient;
    private readonly Func<ChromeProfile, CancellationToken, Task> _tryAutoGoogleLogin;
    private readonly Func<Task> _pauseQuotaPolling;
    private readonly Func<Task> _resumeQuotaPolling;
    private readonly Func<bool, bool, CancellationToken, Task> _refreshConnections;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ProviderConnection>>> _refreshConnectionsLightweight;
    private readonly Action<string> _setStatus;
    private readonly Action<string, ToastType, int> _showToast;
    private readonly Action<ProviderKind, bool> _setCardWorkflowInProgress;
    private readonly Action<ProviderKind> _setSelectedApiKeyProvider;
    private readonly Action<ProviderKind, string>? _onApiKeySaved;
    private readonly Func<CancellationToken, Task<OpenRouterPkceResult>> _openRouterPkceFlow;
    private readonly Func<int> _getDashboardPort;
    private readonly Func<string> _getDashboardBaseUrl;
    private readonly Func<ChromeProfile, CancellationToken, Task<Func<Task<string?>>?>> _getTotpGenerator;
    private readonly Action<Exception> _setError;
    private readonly Action _stateChanged;

    private bool _isInProgress;
    private ProviderKind? _currentProvider;

    public ProviderWorkflowService(
        IProviderConnectionRenameService? renameService = null,
        IProviderApiKeyService? apiKeyService = null,
        Func<ChromeProfile?>? selectedProfileGetter = null,
        Func<ChromeInstallation?>? installationGetter = null,
        Func<ChromeInstallation, ChromeProfile, Uri, CancellationToken, Task<ChromeManagedSession>>? launchManagedChrome = null,
        Func<ChromeProfile, string, Task>? launchUrl = null,
        Func<string, Task>? launchUrlAsync = null,
        Func<Task<IRouterApiClient>>? createApiClient = null,
        Func<ChromeProfile, CancellationToken, Task>? tryAutoGoogleLogin = null,
        Func<Task>? pauseQuotaPolling = null,
        Func<Task>? resumeQuotaPolling = null,
        Func<bool, bool, CancellationToken, Task>? refreshConnections = null,
        Func<CancellationToken, Task<IReadOnlyList<ProviderConnection>>>? refreshConnectionsLightweight = null,
        Action<string>? statusCallback = null,
        Action<string, ToastType, int>? showToast = null,
        Action<ProviderKind, bool>? setCardWorkflowInProgress = null,
        Action<ProviderKind>? setSelectedApiKeyProvider = null,
        Action<ProviderKind, string>? onApiKeySaved = null,
        Func<CancellationToken, Task<OpenRouterPkceResult>>? openRouterPkceFlow = null,
        Func<int>? getDashboardPort = null,
        Func<string>? getDashboardBaseUrl = null,
        Func<ChromeProfile, CancellationToken, Task<Func<Task<string?>>?>>? getTotpGenerator = null,
        Action<Exception>? setError = null,
        Action? stateChanged = null)
    {
        _renameService = renameService ?? new ProviderConnectionRenameService();
        _apiKeyService = apiKeyService ?? new ProviderApiKeyService(new DpapiSecretVault());
        _selectedProfileGetter = selectedProfileGetter ?? (() => null);
        _installationGetter = installationGetter ?? (() => null);
        _launchManagedChrome = launchManagedChrome ?? ((i, p, u, c) => Task.FromException<ChromeManagedSession>(new InvalidOperationException("No managed chrome launcher.")));
        _launchUrl = launchUrl ?? ((p, u) => Task.CompletedTask);
        _launchUrlAsync = launchUrlAsync ?? (u => Task.CompletedTask);
        _createApiClient = createApiClient ?? (() => Task.FromException<IRouterApiClient>(new InvalidOperationException("No api factory.")));
        _tryAutoGoogleLogin = tryAutoGoogleLogin ?? ((p, c) => Task.CompletedTask);
        var rawPause = pauseQuotaPolling ?? (() => Task.CompletedTask);
        var rawResume = resumeQuotaPolling ?? (() => Task.CompletedTask);
        var rawRefresh = refreshConnections ?? ((s, f, c) => Task.CompletedTask);
        var rawRefreshLightweight = refreshConnectionsLightweight ?? (new Func<CancellationToken, Task<IReadOnlyList<ProviderConnection>>>(ct => Task.FromResult<IReadOnlyList<ProviderConnection>>(Array.Empty<ProviderConnection>())));
        _pauseQuotaPolling = () => SafeInvokeAsync(rawPause, "pauseQuotaPolling", CancellationToken.None);
        _resumeQuotaPolling = () => SafeInvokeAsync(rawResume, "resumeQuotaPolling", CancellationToken.None);
        _refreshConnections = (s, f, c) => SafeInvokeAsync(() => rawRefresh(s, f, c), "refreshConnections", c);
        _refreshConnectionsLightweight = ct => SafeInvokeAsync<IReadOnlyList<ProviderConnection>>(() => rawRefreshLightweight(ct), "refreshConnectionsLightweight", ct);
        var rawStatus = statusCallback ?? (_ => { });
        var rawToast = showToast ?? ((m, t, i) => { });
        var rawCard = setCardWorkflowInProgress ?? ((k, v) => { });
        var rawSelected = setSelectedApiKeyProvider ?? (_ => { });
        var rawState = stateChanged ?? (() => { });
        var rawError = setError ?? (_ => { });
        _setStatus = msg => SafeInvoke(() => rawStatus(msg), "status");
        _showToast = (m, t, i) => SafeInvoke(() => rawToast(m, t, i), "toast");
        _setCardWorkflowInProgress = (k, v) => SafeInvoke(() => rawCard(k, v), "cardWorkflow");
        _setSelectedApiKeyProvider = k => SafeInvoke(() => rawSelected(k), "selectedApiKeyProvider");
        _stateChanged = () => SafeInvoke(rawState, "stateChanged");
        _setError = ex => SafeInvoke(() => rawError(ex), "setError");
        _onApiKeySaved = onApiKeySaved;
        _openRouterPkceFlow = openRouterPkceFlow ?? (c => Task.FromException<OpenRouterPkceResult>(new InvalidOperationException("No pkce flow.")));
        _getDashboardPort = getDashboardPort ?? (() => 20128);
        _getDashboardBaseUrl = getDashboardBaseUrl ?? (() => "http://localhost:20128");
        _getTotpGenerator = getTotpGenerator ?? ((p, c) => Task.FromResult<Func<Task<string?>>?>(null));
    }

    public bool IsInProgress
    {
        get => _isInProgress;
        internal set
        {
            if (_isInProgress == value) return;
            _isInProgress = value;
            _stateChanged();
        }
    }

    public ProviderKind? CurrentProvider
    {
        get => _currentProvider;
        internal set
        {
            if (Equals(_currentProvider, value)) return;
            _currentProvider = value;
            _stateChanged();
        }
    }

    public CancellationTokenSource? WorkflowCancellation { get; internal set; }
    public Dictionary<string, ProviderConnection> WorkflowExistingConnections { get; internal set; } =
        new(StringComparer.Ordinal);
    public DateTimeOffset WorkflowStartedAt { get; internal set; }

    private async Task SafeInvokeAsync(Func<Task> action, string callbackName, CancellationToken cancellationToken = default)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Providers",
                "WorkflowCallbackFailed",
                "Workflow async callback failed",
                new { callback = callbackName, error = ex.Message });
        }
    }

    private async Task<T> SafeInvokeAsync<T>(Func<Task<T>> action, string callbackName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Providers",
                "WorkflowCallbackFailed",
                "Workflow async callback failed",
                new { callback = callbackName, error = ex.Message });
            return default!;
        }
    }

    private void SafeInvoke(Action action, string callbackName)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Providers",
                "WorkflowCallbackFailed",
                "Workflow UI callback failed",
                new { callback = callbackName, error = ex.Message });
        }
    }

    public void Cancel()
    {
        if (WorkflowCancellation is { IsCancellationRequested: false } cancellation)
        {
            cancellation.Cancel();
        }
    }

    public async Task OpenProviderForTestAsync(ProviderKind provider) => await OpenProviderAsync(provider);

    public async Task OpenProviderAsync(ProviderKind provider)
    {
        if (IsInProgress) return;

        var definition = ProviderCatalog.Get(provider);
        using var trace = TraceScope.Begin("Providers", "OpenProvider", new { provider = provider.ToString(), workflow = definition.Workflow.ToString() });

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Providers",
            "ProviderWorkflowStarted",
            "User starting provider connection workflow",
            new
            {
                provider = provider.ToString(),
                workflow = definition.Workflow.ToString(),
                profile_name = _selectedProfileGetter()?.Name,
                profile_id = _selectedProfileGetter()?.Id
            });

        var selectedProfile = _selectedProfileGetter();
        if (selectedProfile is null)
        {
            _setStatus("Hãy chọn Chrome profile trước.");
            _showToast(_setStatus is null ? string.Empty : "Hãy chọn Chrome profile trước.", ToastType.Warning, 0);
            return;
        }

        using var workflowCancellation = new CancellationTokenSource();
        WorkflowCancellation = workflowCancellation;
        IsInProgress = true;
        _setCardWorkflowInProgress(provider, true);

        bool quotaPollingPaused = false;
        try
        {
            var cancellationToken = workflowCancellation.Token;
            var api = await _createApiClient();
            if (definition.Workflow is WorkflowKind.OAuth or WorkflowKind.DeviceCode)
            {
                await _pauseQuotaPolling();
                quotaPollingPaused = true;
            }
            switch (definition.Workflow)
            {
                case WorkflowKind.ApiKey:
                    _setSelectedApiKeyProvider(provider);
                    CurrentProvider = null;
                    await _launchUrlAsync(definition.QuickLink);
                    _setStatus($"Đã mở {definition.DisplayName}. Lấy key rồi dán vào ô bảo mật bên dưới và bấm Lưu API key.");
                    _showToast(_setStatus is null ? string.Empty : $"Đã mở {definition.DisplayName}. Lấy key rồi dán vào ô bảo mật bên dưới và bấm Lưu API key.", ToastType.Info, 5);
                    break;
                case WorkflowKind.DeviceCode:
                    await RunDeviceCodeWorkflowAsync(api, provider, cancellationToken);
                    break;
                case WorkflowKind.OAuth:
                    await RunOAuthWorkflowAsync(api, provider, cancellationToken);
                    break;
            }
        }
        catch (OperationCanceledException) when (workflowCancellation.IsCancellationRequested)
        {
            ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Providers", "ProviderWorkflowCancelled", "Provider workflow cancelled by user", new { provider = provider.ToString() });
            ObservabilityHub.Instance.IncrementCounter("provider.workflow.cancelled", tags: new Dictionary<string, string> { ["provider"] = provider.ToString() });
            CurrentProvider = null;
            WorkflowExistingConnections.Clear();
            _setStatus($"Đã hủy thao tác thêm {definition.DisplayName}. Bạn có thể thử lại.");
            _showToast($"Đã hủy thao tác thêm {definition.DisplayName}. Bạn có thể thử lại.", ToastType.Warning, 5);
        }
        catch (Exception exception)
        {
            ObservabilityHub.Instance.IncrementCounter("provider.workflow.failed", tags: new Dictionary<string, string> { ["provider"] = provider.ToString() });
            ObservabilityHub.Instance.LogError("Providers", "ProviderWorkflowFailed", exception, new { provider = provider.ToString(), workflow = definition.Workflow.ToString() });
            CurrentProvider = null;
            WorkflowExistingConnections.Clear();
            _setError(exception);
        }
        finally
        {
            if (quotaPollingPaused)
            {
                try { await _resumeQuotaPolling(); }
                catch (Exception ex)
                {
                    ObservabilityHub.Instance.LogEvent(LogLevel.Warning, "Providers", "QuotaPollingResumeFailed", "Failed to resume quota polling after workflow", new { provider = provider.ToString(), error = ex.Message });
                }
            }

            ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Providers", "ProviderWorkflowFinished", "Provider workflow completed", new { provider = provider.ToString() });

            if (ReferenceEquals(WorkflowCancellation, workflowCancellation))
            {
                WorkflowCancellation = null;
            }

            _setCardWorkflowInProgress(provider, false);
            IsInProgress = false;
        }
    }

    public async Task<bool> ConnectOpenRouterOAuthAsync(CancellationToken cancellationToken = default)
    {
        var selectedProfile = _selectedProfileGetter();
        if (selectedProfile is null)
        {
            _setStatus("Hãy chọn Chrome profile trước.");
            _showToast("Hãy chọn Chrome profile trước.", ToastType.Warning, 0);
            return false;
        }

        if (IsInProgress) return false;

        using var workflowCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        WorkflowCancellation = workflowCancellation;
        IsInProgress = true;
        var cardProvider = ProviderKind.OpenRouter;
        _setCardWorkflowInProgress(cardProvider, true);
        _setStatus("Đang mở OpenRouter OAuth…");

        try
        {
            OpenRouterPkceResult flowResult;
            try
            {
                flowResult = await _openRouterPkceFlow(workflowCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                _setStatus("Đã hủy OpenRouter OAuth.");
                _showToast("Đã hủy OpenRouter OAuth.", ToastType.Warning, 0);
                return false;
            }
            catch (TimeoutException)
            {
                _setStatus("Hết thời gian chờ OpenRouter OAuth. Thử lại.");
                _showToast("Hết thời gian chờ OpenRouter OAuth. Thử lại.", ToastType.Error, 0);
                return false;
            }
            catch (Exception exception)
            {
                _setStatus($"OpenRouter OAuth thất bại: {exception.Message}");
                _showToast($"OpenRouter OAuth thất bại: {exception.Message}", ToastType.Error, 0);
                return false;
            }

            if (!flowResult.Success)
            {
                _setStatus($"OpenRouter OAuth thất bại: {flowResult.ErrorMessage}");
                _showToast($"OpenRouter OAuth thất bại: {flowResult.ErrorMessage}", ToastType.Error, 0);
                return false;
            }

            _setSelectedApiKeyProvider(ProviderKind.OpenRouter);
            return await _addApiKeyInternal(ProviderKind.OpenRouter, flowResult.ApiKey!);
        }
        finally
        {
            if (ReferenceEquals(WorkflowCancellation, workflowCancellation))
            {
                WorkflowCancellation = null;
            }
            _setCardWorkflowInProgress(cardProvider, false);
            IsInProgress = false;
        }
    }

    private async Task<bool> _addApiKeyInternal(ProviderKind provider, string apiKey)
    {
        // Delegate to VM-provided add api key via refresh? We need API key service. Implement minimal using service.
        var definition = ProviderCatalog.Get(provider);
        if (definition.Workflow != WorkflowKind.ApiKey)
        {
            _setStatus($"{definition.DisplayName} không dùng API key.");
            _showToast($"{definition.DisplayName} không dùng API key.", ToastType.Warning, 0);
            return false;
        }

        _setSelectedApiKeyProvider(provider);
        var profile = _selectedProfileGetter();
        if (profile is null)
        {
            _setStatus("Hãy chọn Chrome profile trước.");
            return false;
        }

        apiKey = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _setStatus("Hãy dán API key vào ô bảo mật.");
            _showToast("Hãy dán API key vào ô bảo mật.", ToastType.Warning, 0);
            return false;
        }

        try
        {
            async Task<IRouterApiClient> CreateApiClientWrapper() => await _createApiClient();
            var serviceResult = await _apiKeyService.AddOrUpdateAsync(
                provider, apiKey, profile, CreateApiClientWrapper, CancellationToken.None);

            var connection = serviceResult.Connection;
            var testResult = serviceResult.TestResult;
            string message;
            if (serviceResult.Created)
            {
                message = testResult.Valid
                    ? $"Đã thêm {definition.DisplayName} cho {profile.Name}, priority {connection.Priority}."
                    : $"Đã lưu {definition.DisplayName} cho {profile.Name}, nhưng kiểm tra kết nối thất bại.";
            }
            else
            {
                message = testResult.Valid
                    ? $"{definition.DisplayName} key updated in 9Router for {profile.Name}; local key saved."
                    : $"{definition.DisplayName} key saved for {profile.Name}, but the connection test failed.";
            }
            _setStatus(message);
            _showToast(message, testResult.Valid ? ToastType.Success : ToastType.Warning, 5);
            if (_onApiKeySaved is not null)
            {
                SafeInvoke(() => _onApiKeySaved(provider, apiKey), "onApiKeySaved");
            }
            await _refreshConnections(false, true, CancellationToken.None);
            return true;
        }
        catch (Exception exception)
        {
            _setError(exception);
            return false;
        }
    }

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

        await _tryAutoGoogleLogin(selectedProfile, cancellationToken);

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
