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
        "www.google.com"
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

            // Validate entry page origin
            if (!AllowedEntryHosts.Contains(state.PageUri.Host))
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

            // Wait for page to render expected fields (identifier page may show email field after delay)
            if (!state.HasEmailField && !state.HasPasswordField && !state.HasTotpField && !state.Has2FAMethodPicker)
            {
                var waitDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
                while (DateTimeOffset.UtcNow < waitDeadline)
                {
                    await Task.Delay(500, totalCts.Token);
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);

                    if (state.HasEmailField || state.HasPasswordField || state.HasTotpField || state.Has2FAMethodPicker)
                    {
                        break;
                    }
                }
            }

            // Email step
            if (state.HasEmailField)
            {
                await FillWithTimeoutAsync(browser, GoogleLoginField.Email, credential.Email, totalCts.Token);
                await SubmitWithTimeoutAsync(browser, GoogleLoginField.Email, totalCts.Token);

                // Read state after email submission.
                state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);

                // Validate we're still on allowed origin
                if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.UnsupportedPage(
                        $"Navigation to unexpected origin: {state.PageUri.Host}.");
                }

                // Check for manual challenge - wait for user to complete it
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

                // Check for completion
                if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.Success();
                }
            }

            // Password step
            if (state.HasPasswordField)
            {
                await FillWithTimeoutAsync(browser, GoogleLoginField.Password, credential.Password, totalCts.Token);
                await SubmitWithTimeoutAsync(browser, GoogleLoginField.Password, totalCts.Token);

                // Read state after password submission.
                state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);

                // Validate origin
                if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.UnsupportedPage(
                        $"Navigation to unexpected origin: {state.PageUri.Host}.");
                }

                // Check for manual challenge - wait for user to complete it
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

                // Check for completion
                if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.Success();
                }

                // Check for 2FA method picker after password submission
                if (state.Has2FAMethodPicker)
                {
                    var methodSelected = await TrySelectAuthenticatorMethodWithTimeoutAsync(browser, totalCts.Token);
                    if (!methodSelected)
                    {
                        return GoogleLoginResult.UnsupportedPage(
                            "Could not select Authenticator method from 2FA picker.");
                    }

                    DebugConsole.WriteLine($"[GoogleLogin] Selected Authenticator method, reading state...");

                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);

                    // Check origin after method selection
                    if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                    {
                        return GoogleLoginResult.UnsupportedPage(
                            $"Navigation to unexpected origin: {state.PageUri.Host}.");
                    }
                }
            }

            // TOTP step
            if (state.HasTotpField)
            {
                // Generate TOTP code just-in-time
                string totpCode;
                try
                {
                    totpCode = GoogleTotpGenerator.Generate(
                        credential.TotpSecret,
                        DateTimeOffset.UtcNow,
                        digits: 6,
                        periodSeconds: 30);
                }
                catch
                {
                    return GoogleLoginResult.InvalidCredentials();
                }

                await FillWithTimeoutAsync(browser, GoogleLoginField.Totp, totpCode, totalCts.Token);

                // Clear the TOTP code from memory (best effort)
                totpCode = string.Empty;

                await SubmitWithTimeoutAsync(browser, GoogleLoginField.Totp, totalCts.Token);

                // Read final state.
                state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);

                // Validate origin
                if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.UnsupportedPage(
                        $"Navigation to unexpected origin: {state.PageUri.Host}.");
                }

                // Check for manual challenge - wait for user to complete it
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

                // Check for completion
                if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.Success();
                }
            }

            // 2FA method picker step - when Google shows method selection instead of TOTP input
            if (state.Has2FAMethodPicker)
            {
                var methodSelected = await TrySelectAuthenticatorMethodWithTimeoutAsync(browser, totalCts.Token);
                if (!methodSelected)
                {
                    return GoogleLoginResult.UnsupportedPage(
                        "Could not select Authenticator method from 2FA picker.");
                }

                // Read state after method selection - should now show TOTP field
                state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);

                // Validate origin
                if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.UnsupportedPage(
                        $"Navigation to unexpected origin: {state.PageUri.Host}.");
                }

                // Now process TOTP step
                if (state.HasTotpField)
                {
                    string totpCode;
                    try
                    {
                        totpCode = GoogleTotpGenerator.Generate(
                            credential.TotpSecret,
                            DateTimeOffset.UtcNow,
                            digits: 6,
                            periodSeconds: 30);
                    }
                    catch
                    {
                        return GoogleLoginResult.InvalidCredentials();
                    }

                    await FillWithTimeoutAsync(browser, GoogleLoginField.Totp, totpCode, totalCts.Token);

                    // Clear the TOTP code from memory (best effort)
                    totpCode = string.Empty;

                    await SubmitWithTimeoutAsync(browser, GoogleLoginField.Totp, totalCts.Token);

                    // Read final state.
                    state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);

                    // Validate origin
                    if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                    {
                        return GoogleLoginResult.UnsupportedPage(
                            $"Navigation to unexpected origin: {state.PageUri.Host}.");
                    }

                    // Check for manual challenge
                    if (state.HasManualChallenge)
                    {
                        return GoogleLoginResult.ManualInterventionRequired(
                            "Manual challenge detected after TOTP submission.");
                    }

                    // Check for completion
                    if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                    {
                        return GoogleLoginResult.Success();
                    }
                }
            }

            // If we've exhausted all fields and still no completion signal
            if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
            {
                return GoogleLoginResult.Success();
            }

            // Unrecognized field combination
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
    /// <returns>The new page state after resolution, or null if timeout/cancellation.</returns>
    private static async Task<GoogleLoginPageState?> WaitForManualChallengeResolutionAsync(
        IGoogleLoginBrowser browser,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5);

        DebugConsole.WriteLine("[GoogleLogin] Manual challenge detected, waiting for user to complete...");

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(2000, cancellationToken);
            var state = await ReadStateWithTimeoutAsync(browser, cancellationToken);

            if (!state.HasManualChallenge)
            {
                DebugConsole.WriteLine("[GoogleLogin] Manual challenge resolved, resuming automation");
                return state;
            }
        }

        DebugConsole.WriteLine("[GoogleLogin] Manual challenge resolution timeout");
        return null;
    }

    private static bool IsBrowserDisconnectException(Exception ex)
    {
        // Check for common disconnect exception types
        return ex is ObjectDisposedException
            || ex is InvalidOperationException && ex.Message.Contains("disconnect", StringComparison.OrdinalIgnoreCase)
            || ex is InvalidOperationException && ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase);
    }
}
