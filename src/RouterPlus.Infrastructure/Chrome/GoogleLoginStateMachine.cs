using RouterPlus.Core.Security;

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
        "accounts.google.com"
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

            // Check for manual challenge immediately
            if (state.HasManualChallenge)
            {
                return GoogleLoginResult.ManualInterventionRequired(
                    "Manual challenge detected (CAPTCHA, passkey, or security verification).");
            }

            // Check for completion signal (already logged in)
            if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
            {
                return GoogleLoginResult.Success();
            }

            // Email step
            if (state.HasEmailField)
            {
                await FillWithTimeoutAsync(browser, GoogleLoginField.Email, credential.Email, totalCts.Token);
                await SubmitWithTimeoutAsync(browser, GoogleLoginField.Email, totalCts.Token);

                // Read state after email submission
                state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);

                // Validate we're still on allowed origin
                if (!AllowedEntryHosts.Contains(state.PageUri.Host) && !AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.UnsupportedPage(
                        $"Navigation to unexpected origin: {state.PageUri.Host}.");
                }

                // Check for manual challenge
                if (state.HasManualChallenge)
                {
                    return GoogleLoginResult.ManualInterventionRequired(
                        "Manual challenge detected after email submission.");
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

                // Read state after password submission
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
                        "Manual challenge detected after password submission.");
                }

                // Check for completion
                if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
                {
                    return GoogleLoginResult.Success();
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

                // Read final state
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

            // If we've exhausted all fields and still no completion signal
            if (state.HasCompletionSignal && AllowedCompletionHosts.Contains(state.PageUri.Host))
            {
                return GoogleLoginResult.Success();
            }

            // Unrecognized field combination
            return GoogleLoginResult.UnsupportedPage(
                "Unrecognized page state. No email, password, TOTP field, or completion signal found.");
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
        catch (InvalidOperationException)
        {
            return GoogleLoginResult.UnsupportedPage(
                "Google sign-in page could not be controlled safely.");
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

    private static async Task SubmitWithTimeoutAsync(
        IGoogleLoginBrowser browser,
        GoogleLoginField field,
        CancellationToken cancellationToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(StepTimeout);

        await browser.SubmitAsync(field, stepCts.Token);
    }

    private static bool IsBrowserDisconnectException(Exception ex)
    {
        // Check for common disconnect exception types
        return ex is ObjectDisposedException
            || ex is InvalidOperationException && ex.Message.Contains("disconnect", StringComparison.OrdinalIgnoreCase)
            || ex is InvalidOperationException && ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase);
    }
}
