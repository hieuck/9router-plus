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
    public async Task<bool> AutoGetKeyAsync(ProviderKind provider, CancellationToken cancellationToken = default)
    {
        SelectedApiKeyProvider = provider;

        var profile = SelectedProfile;
        if (profile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            ShowToast(StatusText, ToastType.Warning);
            return false;
        }

        var credential = await _autoGetKeyCredentials(profile, cancellationToken);
        if (credential is null)
        {
            // Try to proceed - some flows may work with existing logged-in session
            StatusText = "Không tìm thấy Google credential trong Vault. Thử tiếp tục với session Chrome hiện tại nếu đã đăng nhập.";
            ShowToast(StatusText, ToastType.Info);
        }

        if (provider == ProviderKind.Ollama)
        {
            if (credential is null)
            {
                StatusText = "Không có thông tin Google trong Vault. Vui lòng thêm credential để đăng nhập Ollama.";
                ShowToast(StatusText, ToastType.Warning);
                return false;
            }

            var ollamaResult = await _ollamaKeyFlow(profile, credential, cancellationToken);
            if (!ollamaResult.Success)
            {
                StatusText = $"Không lấy được Ollama key: {ollamaResult.ErrorMessage}";
                ShowToast(StatusText, ToastType.Error);
                return false;
            }

            return await AddApiKeyAsync(ProviderKind.Ollama, ollamaResult.ApiKey!);
        }
        else if (provider == ProviderKind.Agnes)
        {
            if (credential is null)
            {
                StatusText = "Không có thông tin Google trong Vault. Vui lòng thêm credential để đăng nhập Agnes.";
                ShowToast(StatusText, ToastType.Warning);
                return false;
            }

            var agnesResult = await _agnesKeyFlow(profile, credential, cancellationToken);
            if (!agnesResult.Success)
            {
                StatusText = $"Không lấy được Agnes key: {agnesResult.ErrorMessage}";
                ShowToast(StatusText, ToastType.Error);
                return false;
            }

            return await AddApiKeyAsync(ProviderKind.Agnes, agnesResult.ApiKey!);
        }
        else if (provider == ProviderKind.OpenRouter)
        {
            if (credential is null)
            {
                StatusText = "Không có thông tin Google trong Vault. Vui lòng thêm credential để đăng nhập OpenRouter.";
                ShowToast(StatusText, ToastType.Warning);
                return false;
            }

            var flowResult = await _openRouterKeyFlow(profile, credential, cancellationToken);
            if (!flowResult.Success)
            {
                StatusText = $"Không lấy được OpenRouter key: {flowResult.ErrorMessage}";
                ShowToast(StatusText, ToastType.Error);
                return false;
            }

            return await AddApiKeyAsync(ProviderKind.OpenRouter, flowResult.ApiKey!);
        }
        else if (provider == ProviderKind.Poolside)
        {
            if (credential is null)
            {
                StatusText = "Không có thông tin Google trong Vault. Vui lòng thêm credential để đăng nhập Poolside.";
                ShowToast(StatusText, ToastType.Warning);
                return false;
            }
            try
            {
                var result = await GetPoolsideKeyAsync(profile, credential, cancellationToken);
                if (!result.Success || string.IsNullOrWhiteSpace(result.ApiKey))
                {
                    StatusText = $"Không lấy được Poolside key: {result.ErrorMessage ?? "Unknown error"}";
                    ShowToast(StatusText, ToastType.Error);
                    return false;
                }
                return await AddApiKeyAsync(ProviderKind.Poolside, result.ApiKey);
            }
            catch (Exception ex)
            {
                StatusText = $"Lỗi lấy Poolside key: {ex.Message}";
                ShowToast(StatusText, ToastType.Error);
                return false;
            }
        }
        else if (provider == ProviderKind.Bazaarlink)
        {
            if (credential is null)
            {
                StatusText = "Không có thông tin Google trong Vault. Vui lòng thêm credential để đăng nhập Bazaarlink.";
                ShowToast(StatusText, ToastType.Warning);
                return false;
            }
            try
            {
                var result = await GetBazaarlinkKeyAsync(profile, credential, cancellationToken);
                if (!result.Success || string.IsNullOrWhiteSpace(result.ApiKey))
                {
                    StatusText = $"Không lấy được Bazaarlink key: {result.ErrorMessage ?? "Unknown error"}";
                    ShowToast(StatusText, ToastType.Error);
                    return false;
                }
                return await AddApiKeyAsync(ProviderKind.Bazaarlink, result.ApiKey);
            }
            catch (Exception ex)
            {
                StatusText = $"Lỗi lấy Bazaarlink key: {ex.Message}";
                ShowToast(StatusText, ToastType.Error);
                return false;
            }
        }
        else
        {
            StatusText = $"Provider {provider} không hỗ trợ tự lấy key.";
            ShowToast(StatusText, ToastType.Warning);
            return false;
        }
    }

    public Task<bool> ConnectOpenRouterOAuthAsync(CancellationToken cancellationToken = default) =>
        _providerWorkflowService.ConnectOpenRouterOAuthAsync(cancellationToken);

    public Task<bool> AddApiKeyAsync(string apiKey) =>
        AddApiKeyAsync(SelectedApiKeyProvider, apiKey);

    public async Task<bool> AddApiKeyAsync(ProviderKind provider, string apiKey)
    {
        var definition = ProviderCatalog.Get(provider);
        if (definition.Workflow != WorkflowKind.ApiKey)
        {
            StatusText = $"{definition.DisplayName} không dùng API key.";
            ShowToast(StatusText, ToastType.Warning);
            return false;
        }

        SelectedApiKeyProvider = provider;
        var profile = SelectedProfile;
        if (profile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return false;
        }

        apiKey = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText = "Hãy dán API key vào ô bảo mật.";
            ShowToast(StatusText, ToastType.Warning);
            return false;
        }

        try
        {
            async Task<IRouterApiClient> CreateApiClientWrapper() => await CreateApiClientAsync();
            var serviceResult = await _providerApiKeyService.AddOrUpdateAsync(
                provider,
                apiKey,
                profile,
                CreateApiClientWrapper,
                CancellationToken.None);

            if (Equals(SelectedProfile, profile))
            {
                var card = GetProviderCard(provider);
                card.ApiKeyValue = apiKey;
                card.MarkApiKeySaved();
            }

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
            StatusText = message;
            ShowToast(message, testResult.Valid ? ToastType.Success : ToastType.Warning, 5);
            // Background refresh after UI feedback
            await RefreshConnectionStatusesAsync(showStatus: false, forceLog: true);
            return true;
        }
        catch (Exception exception)
        {
            SetError(exception);
            return false;
        }
    }

    private ProviderCardViewModel GetProviderCard(ProviderKind provider) =>
        ProviderCards.First(card => card.Kind == provider);

    private void UpdateProviderCardStatuses()
    {
        var row = SelectedProfileRow;
        foreach (var card in ProviderCards)
        {
            var status = row?.ProviderStatuses.FirstOrDefault(item => item.Definition.Kind == card.Kind);
            card.UpdateProviderStatus(status);
        }
    }

    internal Task OpenProviderForTestAsync(ProviderKind provider) => _providerWorkflowService.OpenProviderForTestAsync(provider);

    private Task OpenProviderAsync(ProviderKind provider) => _providerWorkflowService.OpenProviderAsync(provider);
    private Task CancelWorkflowAsync()
    {
        if (_providerWorkflowService.WorkflowCancellation is { IsCancellationRequested: false })
        {
            StatusText = "Đang hủy thao tác thêm provider…";
            _providerWorkflowService.Cancel();
        }

        return Task.CompletedTask;
    }

    private int GetDashboardPort()
    {
        if (!Uri.TryCreate(DashboardBaseUrl, UriKind.Absolute, out var dashboardUri))
        {
            return 20128;
        }

        return dashboardUri.IsDefaultPort
            ? dashboardUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80
            : dashboardUri.Port;
    }

    private async Task<Func<Task<string?>>?> CreateTotpGeneratorAsync(ChromeProfile profile, CancellationToken cancellationToken)
    {
        Func<Task<string?>>? totpGenerator = null;
        var vaultSession = await _googleLoginVaultStore.TryOpenRememberedAsync(_googleLoginVaultPaths.VaultPath, cancellationToken);
        if (vaultSession is not null)
        {
            await using (vaultSession)
            {
                var credential = GoogleAccountCredentialResolver.Resolve(vaultSession.Vault, profile, Profiles);
                if (credential is not null && !string.IsNullOrWhiteSpace(credential.TotpSecret))
                {
                    var totpSecret = credential.TotpSecret;
                    totpGenerator = () => Task.FromResult<string?>(GoogleTotpGenerator.Generate(totpSecret, DateTimeOffset.UtcNow));
                }
            }
        }
        return totpGenerator;
    }


    private async Task OpenQuickLinkAsync(ProviderKind provider)
    {
        if (SelectedProfile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            ShowToast(StatusText, ToastType.Warning);
            return;
        }

        try
        {
            var definition = ProviderCatalog.Get(provider);
            if (definition.Workflow == WorkflowKind.ApiKey)
            {
                SelectedApiKeyProvider = provider;
            }
            await LaunchUrlAsync(definition.QuickLink);
            StatusText = $"Đã mở nhanh {definition.DisplayName}.";
            ShowToast(StatusText, ToastType.Success);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    private Task WaitForConnectionAsync() => _providerWorkflowService.WaitForConnectionAsync();
}