using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Bounded state machine that automates OpenRouter API-key acquisition:
/// walks the post-login welcome wizard when it appears, and falls back to
/// creating a key via the keys-page "New Key" popup when it does not.
/// </summary>
public static class OpenRouterOnboardingAutomation
{
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan KeysPageWaitDeadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>How many reloads we allow while waiting for the welcome wizard to appear.</summary>
    private const int MaxWizardReloads = 3;

    /// <summary>Upper bound on wizard steps, so a misdetected wizard cannot loop forever.</summary>
    private const int MaxWizardSteps = 20;

    /// <summary>
    /// Runs the OpenRouter key-acquisition flow.
    /// </summary>
    public static async Task<OpenRouterOnboardingResult> RunAsync(
        IOpenRouterOnboardingBrowser browser,
        string keyName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalCts.CancelAfter(TotalTimeout);

        try
        {
            // Phase A: wait for the keys page to render (post-login landing).
            var state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
            state = await WaitForKeysPageAsync(browser, state, totalCts.Token);

            // Phase B: wait a bounded number of reloads for the welcome wizard to appear.
            var wizardSeen = false;
            for (var attempt = 0; ; attempt++)
            {
                if (state.HasWelcomeWizard && state.HasWelcomeNext)
                {
                    wizardSeen = true;
                    break;
                }

                if (attempt >= MaxWizardReloads)
                {
                    break; // keys page settled without the wizard -> fall back to New Key
                }

                await ReloadWithTimeoutAsync(browser, totalCts.Token);
                state = await ReadStateWithTimeoutAsync(browser, totalCts.Token);
            }

            // Phase C: walk the wizard if it appeared. If it did not expose a key,
            // fall through to the New Key fallback.
            if (wizardSeen)
            {
                var wizardResult = await RunWizardAsync(browser, state, totalCts.Token);
                if (wizardResult is not null)
                {
                    return wizardResult;
                }

                DebugConsole.WriteLine("[OpenRouterOnboarding] Wizard did not expose a key; falling back to New Key.");
            }

            // Phase D: create a key via the keys-page "New Key" popup.
            return await CreateKeyFallbackAsync(browser, keyName, totalCts.Token);
        }
        catch (OperationCanceledException)
        {
            return OpenRouterOnboardingResult.Failed(
                "OpenRouter key automation timeout. The keys page or welcome wizard did not appear.");
        }
        catch (InvalidOperationException ex)
        {
            return OpenRouterOnboardingResult.Failed(
                $"OpenRouter key automation failed: {ex.Message}");
        }
    }

    private static async Task<OpenRouterOnboardingPageState> WaitForKeysPageAsync(
        IOpenRouterOnboardingBrowser browser,
        OpenRouterOnboardingPageState state,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + KeysPageWaitDeadline;
        while (!state.IsOnKeysPage)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new InvalidOperationException("The OpenRouter keys page did not render.");
            }

            await Task.Delay(ReloadDelay, cancellationToken);
            await ReloadWithTimeoutAsync(browser, cancellationToken);
            state = await ReadStateWithTimeoutAsync(browser, cancellationToken);
        }

        return state;
    }

    /// <summary>
    /// Walks the welcome wizard step by step, capturing the API key if the
    /// "Copy Your API Key" panel exposes it. Returns a terminal result only when a
    /// key was captured; returns null when the wizard finished (or stalled) without
    /// exposing a key, so the caller can fall back to creating one via New Key.
    /// </summary>
    private static async Task<OpenRouterOnboardingResult?> RunWizardAsync(
        IOpenRouterOnboardingBrowser browser,
        OpenRouterOnboardingPageState state,
        CancellationToken cancellationToken)
    {
        string? capturedKey = null;

        for (var step = 0; step < MaxWizardSteps; step++)
        {
            bool clicked;

            if (state.HasWelcomeNext)
            {
                clicked = await browser.TryClickNextAsync(cancellationToken);
            }
            else if (state.HasKeyCopyPanel)
            {
                if (!string.IsNullOrEmpty(state.ApiKey))
                {
                    capturedKey = state.ApiKey;
                }

                clicked = await browser.TryClickContinueAsync(cancellationToken);
            }
            else if (state.HasWelcomeContinue)
            {
                clicked = await browser.TryClickContinueAsync(cancellationToken);
            }
            else if (state.HasDoLaterOption)
            {
                clicked = await browser.TryClickDoLaterAsync(cancellationToken);
            }
            else if (state.HasNotSureOption)
            {
                clicked = await browser.TryClickNotSureAsync(cancellationToken);
            }
            else
            {
                // Wizard is present but in an unrecognized step — give up on it.
                DebugConsole.WriteLine("[OpenRouterOnboarding] Wizard is in an unrecognized step; falling back.");
                return null;
            }

            if (!clicked)
            {
                DebugConsole.WriteLine("[OpenRouterOnboarding] Wizard step could not be clicked; falling back.");
                return null;
            }

            state = await ReadStateWithTimeoutAsync(browser, cancellationToken);

            if (!state.HasWelcomeWizard)
            {
                // Wizard completed.
                break;
            }
        }

        return string.IsNullOrEmpty(capturedKey)
            ? null
            : OpenRouterOnboardingResult.Succeeded(capturedKey);
    }

    private static async Task<OpenRouterOnboardingResult> CreateKeyFallbackAsync(
        IOpenRouterOnboardingBrowser browser,
        string keyName,
        CancellationToken cancellationToken)
    {
        var state = await ReadStateWithTimeoutAsync(browser, cancellationToken);

        if (!state.HasNewKeyButton)
        {
            return OpenRouterOnboardingResult.Failed(
                "The \"New Key\" button was not found on the keys page.");
        }

        var newKeyClicked = await browser.TryClickNewKeyAsync(cancellationToken);
        if (!newKeyClicked)
        {
            return OpenRouterOnboardingResult.Failed("Could not click the \"New Key\" button.");
        }

        state = await ReadStateWithTimeoutAsync(browser, cancellationToken);
        if (!state.HasNewKeyNameInput)
        {
            return OpenRouterOnboardingResult.Failed("The \"New Key\" popup did not appear.");
        }

        var created = await browser.TryCreateKeyAsync(keyName, cancellationToken);
        if (!created)
        {
            return OpenRouterOnboardingResult.Failed("Failed to create the OpenRouter API key.");
        }

        state = await ReadStateWithTimeoutAsync(browser, cancellationToken);
        if (!state.HasCreatedKeyPanel || string.IsNullOrEmpty(state.ApiKey))
        {
            return OpenRouterOnboardingResult.Failed(
                "The key was created but its value could not be read from the page.");
        }

        return OpenRouterOnboardingResult.Succeeded(state.ApiKey);
    }

    // ====== Helpers ======

    private static async Task<OpenRouterOnboardingPageState> ReadStateWithTimeoutAsync(
        IOpenRouterOnboardingBrowser browser,
        CancellationToken cancellationToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(StepTimeout);

        return await browser.ReadStateAsync(stepCts.Token);
    }

    private static async Task ReloadWithTimeoutAsync(
        IOpenRouterOnboardingBrowser browser,
        CancellationToken cancellationToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(StepTimeout);

        await browser.ReloadAsync(stepCts.Token);
    }
}