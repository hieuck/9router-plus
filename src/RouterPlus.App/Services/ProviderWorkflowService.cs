using System.Net.Http;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.App.Services;

public sealed partial class ProviderWorkflowService : IProviderWorkflowService
{
    private readonly IProviderConnectionRenameService _renameService;
    private readonly IProviderApiKeyService _apiKeyService;
    private readonly Func<ChromeProfile?> _selectedProfileGetter;
    private readonly Func<ChromeInstallation?> _installationGetter;
    private readonly Func<ChromeInstallation, ChromeProfile, Uri, CancellationToken, Task<ChromeManagedSession>> _launchManagedChrome;
    private readonly Func<ChromeProfile, string, Task> _launchUrl;
    private readonly Func<string, Task> _launchUrlAsync;
    private readonly Func<Task<IRouterApiClient>> _createApiClient;
    private readonly Func<ChromeProfile, CancellationToken, Task<GooglePreLoginResult>> _tryAutoGoogleLogin;
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
    private readonly Func<ChromeProfile, CancellationToken, Task<GoogleLoginCredential?>> _getGoogleCredential;
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
        Func<ChromeProfile, CancellationToken, Task<GooglePreLoginResult>>? tryAutoGoogleLogin = null,
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
        Func<ChromeProfile, CancellationToken, Task<GoogleLoginCredential?>>? getGoogleCredential = null,
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
        _tryAutoGoogleLogin = tryAutoGoogleLogin ?? ((p, c) => Task.FromResult(GooglePreLoginResult.Skipped));
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
        _getGoogleCredential = getGoogleCredential ?? ((p, c) => Task.FromResult<GoogleLoginCredential?>(null));
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
}
