using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Base class for Google OAuth consent flow automation.
/// Delegates Google-specific page detection to GoogleOAuthPageDetector.
/// Subclasses implement provider-specific hooks for non-Google pages.
/// </summary>
public abstract class GoogleOAuthFlowAutomation
{
    protected readonly ChromeCdpClient _client;
    protected readonly string _sessionId;
    protected readonly string _targetId;
    protected readonly string _profileEmail;
    protected readonly Func<Task<string?>>? _totpGenerator;

    protected GoogleOAuthFlowAutomation(
        ChromeCdpClient client,
        string sessionId,
        string targetId,
        string profileEmail,
        Func<Task<string?>>? totpGenerator = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _targetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        _profileEmail = profileEmail ?? throw new ArgumentNullException(nameof(profileEmail));
        _totpGenerator = totpGenerator;
    }

    /// <summary>
    /// Main OAuth consent flow. Calls provider-specific hooks.
    /// </summary>
    public async Task<OAuthConsentResult> WaitAndConsentAsync(
        Uri startUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startUri);

        var deadline = DateTimeOffset.UtcNow + timeout;
        var clickedScreenUrls = new HashSet<string>(StringComparer.Ordinal);
        var totpAttempted = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Detect Google first so provider automation never owns a Google page.
            var googleState = await GoogleOAuthPageDetector.TryDetectAsync(_client, _sessionId, cancellationToken);
            var providerState = googleState is null
                ? await ReadProviderPageStateAsync(cancellationToken)
                : null;


            // Combine states
            var combinedState = new CombinedOAuthPageState
            {
                ProviderState = providerState,
                GoogleState = googleState
            };

            if (!combinedState.IsGoogleOAuthPage)
            {
                LogPageState(combinedState);

                // Check completion (provider-specific)
                var completionCheck = CheckCompletion(combinedState);
                if (completionCheck.IsComplete)
                {
                    return completionCheck.Result!;
                }
            }

            // Handle provider-specific initial button only on provider-owned pages.
            if (!combinedState.IsGoogleOAuthPage && ShouldClickProviderInitialButton(combinedState))
            {
                var screenKey = $"provider-initial:{combinedState.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                var clicked = await TryClickProviderInitialButtonAsync(combinedState, cancellationToken);
                if (clicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: "Could not click provider initial button");
            }

            // Handle account pickers through the owning automation.
            if (ShouldClickAccountPicker(combinedState))
            {
                var screenKey = $"picker:{combinedState.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                var accountClicked = await TryClickAccountPickerAsync(
                    combinedState, cancellationToken);
                if (accountClicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: $"Could not select account '{_profileEmail}' from picker");
            }

            // Handle provider-specific account picker only on provider-owned pages.
            if (IsProviderOwnedPage(combinedState) && ShouldClickProviderAccountPicker(combinedState))
            {
                var screenKey = $"provider-picker:{combinedState.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                var accountClicked = await TryClickProviderAccountPickerAsync(combinedState, cancellationToken);
                if (accountClicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: $"Could not select provider account from picker");
            }

            // Handle Google TOTP
            if (ShouldFillTotp(combinedState) && !totpAttempted)
            {
                totpAttempted = true;

                if (_totpGenerator is not null)
                {
                    var totpCode = await _totpGenerator();
                    if (!string.IsNullOrWhiteSpace(totpCode))
                    {
                        DebugConsole.WriteLine("[GoogleOAuth] Auto-filling TOTP code...");
                        var filled = await GoogleOAuthPageDetector.TryFillTotpAsync(
                            _client, _sessionId, totpCode, cancellationToken);
                        if (filled)
                        {
                            await Task.Delay(2000, cancellationToken);
                            continue;
                        }
                    }
                }

                DebugConsole.WriteLine("[GoogleOAuth] Waiting for manual TOTP entry...");
                await Task.Delay(1000, cancellationToken);
                continue;
            }

            // Handle Google consent button
            if (ShouldClickGoogleConsent(combinedState))
            {
                var screenKey = $"google-consent:{combinedState.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                DebugConsole.WriteLine("[GoogleOAuth] Clicking Google consent button...");
                var clicked = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
                    _client, _sessionId, cancellationToken);
                if (clicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: "Could not click Google consent button");
            }

            // Handle provider-specific consent only on provider-owned pages.
            if (!combinedState.IsGoogleOAuthPage && ShouldClickProviderConsent(combinedState))
            {
                var screenKey = $"provider-consent:{combinedState.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                var clicked = await TryClickProviderConsentButtonAsync(combinedState, cancellationToken);
                if (clicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: "Could not click provider consent button");
            }

            await Task.Delay(500, cancellationToken);
        }

        return new OAuthConsentResult(
            Success: false,
            AlreadyAuthorized: false,
            Message: "Timeout waiting for OAuth consent flow");
    }

    // ========== Abstract methods (must override) ==========

    /// <summary>
    /// Read provider-specific page state (non-Google pages).
    /// </summary>
    protected abstract Task<ProviderOAuthPageState?> ReadProviderPageStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Check if OAuth flow completed.
    /// </summary>
    protected abstract CompletionCheckResult CheckCompletion(CombinedOAuthPageState state);

    /// <summary>
    /// Log page state for debugging.
    /// </summary>
    protected abstract void LogPageState(CombinedOAuthPageState state);

    // ========== Virtual methods (can override) ==========

    /// <summary>
    /// Should click provider-specific initial button (e.g., "Continue with Google" on AWS)?
    /// </summary>
    protected virtual bool ShouldClickProviderInitialButton(CombinedOAuthPageState state) => false;

    /// <summary>
    /// Click provider-specific initial button.
    /// </summary>
    protected virtual Task<bool> TryClickProviderInitialButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
        => Task.FromResult(false);

    /// <summary>
    /// Should click the shared Google account picker?
    /// </summary>
    protected virtual bool ShouldClickAccountPicker(CombinedOAuthPageState state)
        => state.GoogleState?.HasAccountPicker == true;

    /// <summary>
    /// Click the account picker owned by the current automation.
    /// </summary>
    protected virtual Task<bool> TryClickAccountPickerAsync(
        CombinedOAuthPageState state,
        CancellationToken cancellationToken) =>
        GoogleOAuthPageDetector.TryClickAccountAsync(
            _client, _sessionId, _profileEmail, cancellationToken);

    /// <summary>
    /// Should click a provider-specific account picker on a provider-owned page?
    /// </summary>
    protected virtual bool ShouldClickProviderAccountPicker(CombinedOAuthPageState state) => false;

    /// <summary>
    /// Click a provider-specific account picker.
    /// </summary>
    protected virtual Task<bool> TryClickProviderAccountPickerAsync(
        CombinedOAuthPageState state,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);

    internal static bool IsProviderOwnedPage(CombinedOAuthPageState state) =>
        !state.IsGoogleOAuthPage;

    /// <summary>
    /// Should fill TOTP?
    /// </summary>
    protected virtual bool ShouldFillTotp(CombinedOAuthPageState state)
        => state.GoogleState?.HasGoogleTotpInput == true;

    /// <summary>
    /// Should click Google consent button?
    /// </summary>
    protected virtual bool ShouldClickGoogleConsent(CombinedOAuthPageState state)
        => state.GoogleState?.HasGoogleConsentButton == true;

    /// <summary>
    /// Should click provider-specific consent?
    /// </summary>
    protected virtual bool ShouldClickProviderConsent(CombinedOAuthPageState state) => false;

    /// <summary>
    /// Click provider-specific consent button.
    /// </summary>
    protected virtual Task<bool> TryClickProviderConsentButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
        => Task.FromResult(false);

    // ========== Combined state ==========

    /// <summary>
    /// Combined state from provider-specific and Google page detection.
    /// </summary>
    public sealed record CombinedOAuthPageState
    {
        public ProviderOAuthPageState? ProviderState { get; init; }
        public GoogleOAuthPageState? GoogleState { get; init; }

        public string CurrentUrl => ProviderState?.CurrentUrl ?? GoogleState?.CurrentUrl ?? string.Empty;

        public bool IsGoogleOAuthPage => GoogleState != null;
        public bool HasAccountPicker => GoogleState?.HasAccountPicker == true;
        public bool HasGoogleTotpInput => GoogleState?.HasGoogleTotpInput == true;
        public bool HasGoogleConsentButton => GoogleState?.HasGoogleConsentButton == true;
    }

    /// <summary>
    /// Result of completion check.
    /// </summary>
    public record CompletionCheckResult(
        bool IsComplete,
        OAuthConsentResult? Result = null);
}
