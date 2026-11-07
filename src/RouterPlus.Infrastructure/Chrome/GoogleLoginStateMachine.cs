using RouterPlus.Core.Observability;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Diagnostics;
namespace RouterPlus.Infrastructure.Chrome;
/// <summary>
/// Bounded state machine for Google login automation with strict origin guards and timeouts.
/// </summary>
public static class GoogleLoginStateMachine
{
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> AllowedEntryHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.google.com",
        "myaccount.google.com"
    };
    private static readonly HashSet<string> AllowedCompletionHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.google.com",
        "myaccount.google.com",
        "www.google.com",
        "gds.google.com"
    };
    /// <summary>
    /// Runs the bounded Google login flow.
    /// </summary>
    public static async Task<GoogleLoginResult> RunAsync(
        IGoogleLoginBrowser browser,
        GoogleLoginCredential credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(credential);
        using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalCts.CancelAfter(TotalTimeout);
        try
        {
            // Read initial state
            var state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
            // Validate entry page origin (allow OAuth consent/chooser only on Google hosts)
            if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
            {
                return GoogleLoginResult.UnsupportedPage(
                    $"Wrong origin: {state.PageUri.Host}. Expected accounts.google.com.");
            }
            // Check for manual challenge and wait for the user to complete it
            if (state.HasManualChallenge)
            {
                var resolvedState = await WaitForManualChallengeResolutionAsync(browser, totalCts.Token);
                if (resolvedState is null)
                {
                    return GoogleLoginResult.ManualInterventionRequired(
                        "Manual challenge detected. Please complete it manually.");
                }
                state = resolvedState;
            }
            // Check for completion signal (already logged in)
            if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
            {
                return GoogleLoginResult.Success();
            }
            // Wait for page to render expected fields
            if (!state.HasEmailField && !state.HasPasswordField && !state.HasTotpField && !state.Has2FAMethodPicker && !state.HasAccountChooser && !state.HasConsentScreen)
            {
                var waitDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
                while (DateTimeOffset.UtcNow < waitDeadline)
                {
                    await Task.Delay(500, totalCts.Token);
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                    if (state.HasEmailField || state.HasPasswordField || state.HasTotpField || state.Has2FAMethodPicker || state.HasAccountChooser || state.HasConsentScreen)
                    {
                        break;
                    }
                }
            }
            // Handle account chooser if present
            if (state.HasAccountChooser)
            {
                if (await TrySelectAccountWithTimeoutAsync(browser, credential.Email, totalCts.Token))
                {
                    await Task.Delay(1500, totalCts.Token);
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                }
                else
                {
                    await Task.Delay(500, totalCts.Token);
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                    if (state.HasAccountChooser)
                    {
                        await TrySelectAccountWithTimeoutAsync(browser, credential.Email, totalCts.Token);
                        await Task.Delay(1500, totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                    }
                }
            }
            // Handle consent screen early
            if (state.HasConsentScreen)
            {
                var consentClickedEarly = await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                if (consentClickedEarly)
                {
                    await Task.Delay(1500, totalCts.Token);
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                    if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                    {
                        return GoogleLoginResult.Success();
                    }
                }
            }
            // Email step
            if (state.HasEmailField)
            {
                await FillWithTimeoutAsync(browser, GoogleLoginField.Email, credential.Email, totalCts.Token);
                await SubmitWithTimeoutAsync(browser, GoogleLoginField.Email, totalCts.Token);
                state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.UnsupportedPage(
                        $"Navigation to unexpected origin: {state.PageUri.Host}.");
                }
                if (state.HasManualChallenge)
                {
                    var resolvedState = await WaitForManualChallengeResolutionAsync(browser, totalCts.Token);
                    if (resolvedState is null)
                    {
                        return GoogleLoginResult.ManualInterventionRequired(
                            "Manual challenge detected after email submission. Please complete it manually.");
                    }
                    state = resolvedState;
                }
                if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.Success();
                }
                if (state.HasAccountChooser)
                {
                    if (await TrySelectAccountWithTimeoutAsync(browser, credential.Email, totalCts.Token))
                    {
                        await Task.Delay(1500, totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                    }
                    else
                    {
                        await Task.Delay(500, totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasAccountChooser)
                        {
                            await TrySelectAccountWithTimeoutAsync(browser, credential.Email, totalCts.Token);
                            await Task.Delay(1500, totalCts.Token);
                            state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        }
                    }
                }
                if (state.HasConsentScreen)
                {
                    var consentClicked = await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                    if (consentClicked)
                    {
                        await Task.Delay(1500, totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                        {
                            return GoogleLoginResult.Success();
                        }
                    }
                    else
                    {
                        await Task.Delay(1000, totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasConsentScreen)
                        {
                            await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                            await Task.Delay(1500, totalCts.Token);
                            state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                            if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                            {
                                return GoogleLoginResult.Success();
                            }
                        }
                    }
                }
            }
            // Password step
            if (state.HasPasswordField)
            {
                await FillWithTimeoutAsync(browser, GoogleLoginField.Password, credential.Password, totalCts.Token);
                await SubmitWithTimeoutAsync(browser, GoogleLoginField.Password, totalCts.Token);
                state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.UnsupportedPage(
                        $"Navigation to unexpected origin: {state.PageUri.Host}.");
                }
                if (state.HasManualChallenge)
                {
                    var resolvedState = await WaitForManualChallengeResolutionAsync(browser, totalCts.Token);
                    if (resolvedState is null)
                    {
                        return GoogleLoginResult.ManualInterventionRequired(
                            "Manual challenge detected after password submission. Please complete it manually.");
                    }
                    state = resolvedState;
                }
                if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.Success();
                }
                if (state.Has2FAMethodPicker)
                {
                    var methodSelected = await TrySelectAuthenticatorMethodWithTimeoutAsync(browser, totalCts.Token);
                    if (!methodSelected)
                    {
                        return GoogleLoginResult.UnsupportedPage(
                            "Could not select Authenticator method from 2FA picker.");
                    }
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "GoogleLogin",
                        "AuthenticatorMethodSelected",
                        "Selected Authenticator method from 2FA picker",
                        null);
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                    if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                    {
                        return GoogleLoginResult.UnsupportedPage(
                            $"Navigation to unexpected origin: {state.PageUri.Host}.");
                    }
                }
                if (state.HasAccountChooser)
                {
                    if (await TrySelectAccountWithTimeoutAsync(browser, credential.Email, totalCts.Token))
                    {
                        await Task.Delay(1500, totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                    }
                    else
                    {
                        await Task.Delay(500, totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasAccountChooser)
                        {
                            await TrySelectAccountWithTimeoutAsync(browser, credential.Email, totalCts.Token);
                            await Task.Delay(1500, totalCts.Token);
                            state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        }
                    }
                }
                if (!state.HasCompletionSignal && !state.Has2FAMethodPicker && !state.HasManualChallenge && !state.HasTotpField && !state.HasAccountChooser)
                {
                    var consentClicked = await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                    if (consentClicked)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                        {
                            return GoogleLoginResult.Success();
                        }
                    }
                    else
                    {
                        await Task.Delay(1000, totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasConsentScreen)
                        {
                            await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                            await Task.Delay(1500, totalCts.Token);
                            state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                            if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                            {
                                return GoogleLoginResult.Success();
                            }
                        }
                    }
                }
            }
            // TOTP step (direct path)
            if (state.HasTotpField)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info, "GoogleLogin", "TotpStepStarted",
                    "Starting TOTP authentication step",
                    new { page_url = state.PageUri.AbsolutePath });
                var (totpError, stateAfterTotp) = await HandleTotpStepAsync(browser, credential, state, totalCts.Token);
                if (totpError is not null)
                {
                    return totpError;
                }
                state = stateAfterTotp;
                if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.UnsupportedPage(
                        $"Navigation to unexpected origin: {state.PageUri.Host}.");
                }
                if (state.HasManualChallenge)
                {
                    var resolvedState = await WaitForManualChallengeResolutionAsync(browser, totalCts.Token);
                    if (resolvedState is null)
                    {
                        return GoogleLoginResult.ManualInterventionRequired(
                            "Manual challenge detected after TOTP submission. Please complete it manually.");
                    }
                    state = resolvedState;
                }
                if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.Success();
                }
                if (!state.HasCompletionSignal && !state.HasManualChallenge)
                {
                    var consentClicked = await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                    if (consentClicked)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), totalCts.Token);
                        state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                        if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                        {
                            return GoogleLoginResult.Success();
                        }
                    }
                }
            }
            // Handle account chooser/consent if still present
            if (state.HasAccountChooser)
            {
                if (await TrySelectAccountWithTimeoutAsync(browser, credential.Email, totalCts.Token))
                {
                    await Task.Delay(1000, totalCts.Token);
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                }
            }
            if (state.HasConsentScreen)
            {
                var consentClicked = await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                if (consentClicked)
                {
                    await Task.Delay(1000, totalCts.Token);
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                    if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                    {
                        return GoogleLoginResult.Success();
                    }
                }
            }
            // 2FA method picker step
            if (state.Has2FAMethodPicker)
            {
                var methodSelected = await TrySelectAuthenticatorMethodWithTimeoutAsync(browser, totalCts.Token);
                if (!methodSelected)
                {
                    return GoogleLoginResult.UnsupportedPage(
                        "Could not select Authenticator method from 2FA picker.");
                }
                state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.UnsupportedPage(
                        $"Navigation to unexpected origin: {state.PageUri.Host}.");
                }
                if (state.HasTotpField)
                {
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info, "GoogleLogin", "TotpStepStarted",
                        "Starting TOTP authentication step after 2FA method selection",
                        new { page_url = state.PageUri.AbsolutePath });
                    var (totpError, stateAfterTotp) = await HandleTotpStepAsync(browser, credential, state, totalCts.Token);
                    if (totpError is not null)
                    {
                        return totpError;
                    }
                    state = stateAfterTotp;
                    if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                    {
                        return GoogleLoginResult.UnsupportedPage(
                            $"Navigation to unexpected origin: {state.PageUri.Host}.");
                    }
                    if (state.HasManualChallenge)
                    {
                        return GoogleLoginResult.ManualInterventionRequired(
                            "Manual challenge detected after TOTP submission.");
                    }
                    if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                    {
                        return GoogleLoginResult.Success();
                    }
                    if (!state.HasCompletionSignal && !state.HasManualChallenge)
                    {
                        var consentClicked = await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                        if (consentClicked)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), totalCts.Token);
                            state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                            if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                            {
                                return GoogleLoginResult.Success();
                            }
                        }
                    }
                }
            }
            if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
            {
                return GoogleLoginResult.Success();
            }
            if (state.HasAccountChooser)
            {
                if (await TrySelectAccountWithTimeoutAsync(browser, credential.Email, totalCts.Token))
                {
                    await Task.Delay(1000, totalCts.Token);
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                    if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                    {
                        return GoogleLoginResult.Success();
                    }
                }
            }
            if (!state.HasEmailField && !state.HasPasswordField && !state.HasTotpField)
            {
                var consentClicked = await TryClickConsentContinueWithTimeoutAsync(browser, totalCts.Token);
                if (consentClicked)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), totalCts.Token);
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
                    if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                    {
                        return GoogleLoginResult.Success();
                    }
                }
            }
            return GoogleLoginResult.UnsupportedPage(
                $"Unrecognized page state at {state.PageUri.Host}{state.PageUri.AbsolutePath}: " +
                $"email={state.HasEmailField}, password={state.HasPasswordField}, " +
                $"totp={state.HasTotpField}, completion={state.HasCompletionSignal}, " +
                $"manualChallenge={state.HasManualChallenge}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return GoogleLoginResult.Cancelled();
        }
        catch (OperationCanceledException) when (totalCts.Token.IsCancellationRequested)
        {
            return GoogleLoginResult.Timeout();
        }
        catch (Exception ex) when (IsBrowserDisconnectException(ex))
        {
            return GoogleLoginResult.BrowserDisconnected();
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("Google rejected", StringComparison.OrdinalIgnoreCase))
            {
                return GoogleLoginResult.InvalidCredentials();
            }
            return GoogleLoginResult.UnsupportedPage(
                $"Google sign-in page could not be controlled safely: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates and submits a TOTP code with an expiry-window guard and one retry on rejection.
    /// Returns (null, newState) on success; (errorResult, state) if generation fails or code rejected after retry.
    /// </summary>
    private static async Task<(GoogleLoginResult? error, GoogleLoginPageState state)> HandleTotpStepAsync(
        IGoogleLoginBrowser browser,
        GoogleLoginCredential credential,
        GoogleLoginPageState state,
        CancellationToken ct)
    {
        // FR-3: Expiry-window guard — wait if fewer than 5 seconds remain in the current window.
        var secondsLeft = 30 - (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30);
        if (secondsLeft < 5)
        {
            await Task.Delay(TimeSpan.FromSeconds(secondsLeft + 1), ct);
        }

        // FR-2: Typed catch — OperationCanceledException must not be swallowed.
        string totpCode;
        try
        {
            totpCode = GoogleTotpGenerator.Generate(
                credential.TotpSecret,
                DateTimeOffset.UtcNow,
                digits: 6,
                periodSeconds: 30);
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Debug, "GoogleLogin", "TotpCodeGenerated", "TOTP code generated successfully");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error, "GoogleLogin", "TotpGenerationFailed",
                "Failed to generate TOTP code - invalid secret");
            return (GoogleLoginResult.InvalidCredentials(), state);
        }

        await FillWithTimeoutAsync(browser, GoogleLoginField.Totp, totpCode, ct);
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug, "GoogleLogin", "TotpFieldFilled", "TOTP code filled into field");
        totpCode = string.Empty;
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info, "GoogleLogin", "TotpSubmitStarting", "Starting TOTP submit");
        await SubmitWithTimeoutAsync(browser, GoogleLoginField.Totp, ct);
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info, "GoogleLogin", "TotpSubmitted", "TOTP code submitted, waiting for response");
        state = await ReadStateWithTimeoutAsync(browser, ct);

        // FR-4: Single retry on rejection.
        if (state.HasTotpError)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning, "GoogleLogin", "TotpRejected_Retrying",
                "TOTP code rejected, waiting for next window and retrying once");
            var secondsLeftRetry = 30 - (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30);
            await Task.Delay(TimeSpan.FromSeconds(secondsLeftRetry + 1), ct);
            string retryCode;
            try
            {
                retryCode = GoogleTotpGenerator.Generate(
                    credential.TotpSecret,
                    DateTimeOffset.UtcNow,
                    digits: 6,
                    periodSeconds: 30);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Error, "GoogleLogin", "TotpRetryGenerationFailed",
                    "Failed to generate retry TOTP code");
                return (GoogleLoginResult.InvalidCredentials(), state);
            }
            await FillWithTimeoutAsync(browser, GoogleLoginField.Totp, retryCode, ct);
            retryCode = string.Empty;
            await SubmitWithTimeoutAsync(browser, GoogleLoginField.Totp, ct);
            state = await ReadStateWithTimeoutAsync(browser, ct);
            if (state.HasTotpError)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Error, "GoogleLogin", "TotpRejected",
                    "TOTP code rejected by Google after retry - wrong or expired code",
                    new { page_url = state.PageUri.AbsolutePath });
                return (GoogleLoginResult.InvalidCredentials(), state);
            }
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info, "GoogleLogin", "TotpAccepted", "TOTP code accepted",
            new { page_url = state.PageUri.AbsolutePath, has_completion = state.HasCompletionSignal, has_challenge = state.HasManualChallenge });
        return (null, state);
    }

    private static async Task<GoogleLoginPageState> ReadStateWithTimeoutAsync(
        IGoogleLoginBrowser browser,
        CancellationToken cancellationToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(StepTimeout);
        return await browser.ReadStateAsync(stepCts.Token);
    }
    private static async Task FillWithTimeoutAsync(
        IGoogleLoginBrowser browser,
        GoogleLoginField field,
        string value,
        CancellationToken cancellationToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(StepTimeout);
        await browser.FillAsync(field, value, stepCts.Token);
    }
    private static async Task<bool> TrySelectAuthenticatorMethodWithTimeoutAsync(
        IGoogleLoginBrowser browser,
        CancellationToken cancellationToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(StepTimeout);
        return await browser.TrySelectAuthenticatorMethodAsync(stepCts.Token);
    }
    private static async Task<bool> TrySelectAccountWithTimeoutAsync(
        IGoogleLoginBrowser browser,
        string? email,
        CancellationToken cancellationToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(StepTimeout);
        return await browser.TrySelectAccountOnChooserAsync(email, stepCts.Token);
    }
    private static async Task<bool> TryClickConsentContinueWithTimeoutAsync(
        IGoogleLoginBrowser browser,
        CancellationToken cancellationToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(StepTimeout);
        return await browser.TryClickConsentContinueAsync(stepCts.Token);
    }
    private static async Task SubmitWithTimeoutAsync(
        IGoogleLoginBrowser browser,
        GoogleLoginField field,
        CancellationToken cancellationToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(StepTimeout);
        await browser.SubmitAsync(field, stepCts.Token);
    }
    /// <summary>
    /// Waits for manual challenge (CAPTCHA, passkey, etc.) to be resolved by the user.
    /// Polls the page state every 2 seconds until the challenge flag clears or timeout.
    /// </summary>
    private static async Task<GoogleLoginPageState?> WaitForManualChallengeResolutionAsync(
        IGoogleLoginBrowser browser,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5);
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info, "GoogleLogin", "ManualChallengeDetected",
            "Manual challenge detected, waiting for user to complete", null);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(2000, cancellationToken);
            var state = await ReadStateWithTimeoutAsync(browser, cancellationToken);
            if (!state.HasManualChallenge)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info, "GoogleLogin", "ManualChallengeResolved",
                    "Manual challenge resolved, resuming automation", null);
                return state;
            }
        }
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Warning, "GoogleLogin", "ManualChallengeTimeout",
            "Manual challenge resolution timeout", null);
        return null;
    }
    private static bool IsBrowserDisconnectException(Exception ex)
    {
        return ex is ObjectDisposedException
            || ex is InvalidOperationException && ex.Message.Contains("disconnect", StringComparison.OrdinalIgnoreCase)
            || ex is InvalidOperationException && ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase);
    }
}