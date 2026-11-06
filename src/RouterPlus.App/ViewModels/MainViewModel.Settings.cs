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
    public async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(BuildSettings());
            RefreshProfiles();
            MarkSettingsSaved();
            await RefreshConnectionStatusesAsync();
            StatusText = "Đã lưu cài đặt.";
            ShowToast(StatusText, ToastType.Success);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    public async Task SaveWindowPlacementAsync(double left, double top, double width, double height)
    {
        if (!IsValidWindowPlacement(left, top, width, height))
        {
            return;
        }

        var placement = new WindowPlacement(left, top, width, height);
        try
        {
            var settings = await _settingsStore.LoadAsync();
            await _settingsStore.SaveAsync(settings with
            {
                WindowLeft = placement.Left,
                WindowTop = placement.Top,
                WindowWidth = placement.Width,
                WindowHeight = placement.Height
            });
            _savedWindowPlacement = placement;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    /// <summary>
    /// Injectable seam for the Chrome-based OpenRouter key flow (mirrors
    /// <see cref="GoogleLoginAutomation"/>), so tests can drive the flow
    /// without launching a browser.
    /// </summary>
    public Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<OpenRouterKeyFlowOrchestrator.OpenRouterKeyFlowResult>> OpenRouterKeyFlow
    {
        get => _openRouterKeyFlow;
        set => _openRouterKeyFlow = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Injectable seam for the Chrome-based Ollama key flow, so tests can drive the flow
    /// without launching a browser.
    /// </summary>
    public Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<OllamaKeyFlowOrchestrator.OllamaKeyFlowResult>> OllamaKeyFlow
    {
        get => _ollamaKeyFlow;
        set => _ollamaKeyFlow = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Injectable seam for the Chrome-based Agnes key flow, so tests can drive the flow
    /// without launching a browser.
    /// </summary>
    public Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<AgnesKeyFlowOrchestrator.AgnesKeyFlowResult>> AgnesKeyFlow
    {
        get => _agnesKeyFlow;
        set => _agnesKeyFlow = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<PoolsideKeyFlowOrchestrator.PoolsideKeyFlowResult>> PoolsideKeyFlow
    {
        get => _poolsideKeyFlow;
        set => _poolsideKeyFlow = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<BazaarlinkKeyFlowOrchestrator.BazaarlinkKeyFlowResult>> BazaarlinkKeyFlow
    {
        get => _bazaarlinkKeyFlow;
        set => _bazaarlinkKeyFlow = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Injectable seam for loading the Google login credential (from the
    /// Google account vault) used by <see cref="AutoGetKeyAsync"/>.
    /// </summary>
    public Func<ChromeProfile, CancellationToken, Task<GoogleLoginCredential?>> AutoGetKeyCredentials
    {
        get => _autoGetKeyCredentials;
        set => _autoGetKeyCredentials = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Func<CancellationToken, Task<OpenRouterPkceResult>> OpenRouterPkceFlow
    {
        get => _openRouterPkceFlow;
        set => _openRouterPkceFlow = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    public async Task<PoolsideKeyFlowOrchestrator.PoolsideKeyFlowResult> GetPoolsideKeyAsync(
        ChromeProfile profile,
        GoogleLoginCredential credential,
        CancellationToken cancellationToken = default) =>
        await _poolsideKeyFlow(profile, credential, cancellationToken);

    public async Task<BazaarlinkKeyFlowOrchestrator.BazaarlinkKeyFlowResult> GetBazaarlinkKeyAsync(
        ChromeProfile profile,
        GoogleLoginCredential credential,
        CancellationToken cancellationToken = default) =>
        await _bazaarlinkKeyFlow(profile, credential, cancellationToken);

    /// <summary>
    /// Auto-get an API key for the given provider (OpenRouter, Ollama, or Agnes):
    /// opens Chrome, signs in with Google from the vault, deletes old keys, creates
    /// a new one, then saves it into 9Router for the selected profile.
    /// </summary>
}