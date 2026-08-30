namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Current state of the OpenRouter keys/onboarding page, detected via CDP.
/// </summary>
public sealed record OpenRouterOnboardingPageState(
    Uri PageUri,
    bool IsOnKeysPage,
    bool HasWelcomeWizard,
    bool HasWelcomeNext,
    bool HasKeyCopyPanel,
    bool HasWelcomeContinue,
    bool HasDoLaterOption,
    bool HasNotSureOption,
    bool HasNewKeyButton,
    bool HasNewKeyNameInput,
    bool HasCreatedKeyPanel,
    string ApiKey);

/// <summary>
/// Testable browser interface for the OpenRouter onboarding wizard + New Key fallback.
/// Mirrors IGoogleLoginBrowser so the state machine can be unit tested without a browser.
/// </summary>
public interface IOpenRouterOnboardingBrowser : IAsyncDisposable
{
    Task<OpenRouterOnboardingPageState> ReadStateAsync(CancellationToken cancellationToken);

    Task ReloadAsync(CancellationToken cancellationToken);

    /// <summary>Clicks the wizard "Next" button. Returns false if it could not be clicked.</summary>
    Task<bool> TryClickNextAsync(CancellationToken cancellationToken);

    /// <summary>Clicks the wizard "Continue" button. Returns false if it could not be clicked.</summary>
    Task<bool> TryClickContinueAsync(CancellationToken cancellationToken);

    /// <summary>Clicks the "I'll do this later" option. Returns false if it could not be clicked.</summary>
    Task<bool> TryClickDoLaterAsync(CancellationToken cancellationToken);

    /// <summary>Clicks the "Other / Not Sure" option. Returns false if it could not be clicked.</summary>
    Task<bool> TryClickNotSureAsync(CancellationToken cancellationToken);

    /// <summary>Clicks the keys-page "New Key" button. Returns false if it could not be clicked.</summary>
    Task<bool> TryClickNewKeyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fills the New Key popup name field and clicks Create. Returns false if the popup was not present.
    /// </summary>
    Task<bool> TryCreateKeyAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Clicks the Clerk "Sign in with Google" button (icon-only, no innerText) on the
    /// OpenRouter sign-in page. Returns false if the button was not present.
    /// </summary>
    Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken);

    /// <summary>Returns the current page URL, for the orchestrator to detect the Google OAuth redirect.</summary>
    Task<string> GetCurrentUrlAsync(CancellationToken cancellationToken);

    /// <summary>Waits (up to a bounded time) for the URL to leave OpenRouter and reach a Google sign-in page.</summary>
    Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken);

    /// <summary>Waits (up to a bounded time) for the URL to return to an OpenRouter keys page after auth.</summary>
    Task<bool> WaitForOpenRouterKeysAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Navigates to the OpenRouter sign-in page from wherever the managed Chrome
    /// window currently is. Used at the start of the key flow so a stale tab does
    /// not block OAuth. Returns false if the navigation could not be initiated.
    /// </summary>
    Task<bool> NavigateToOpenRouterSignInAsync(CancellationToken cancellationToken);
}