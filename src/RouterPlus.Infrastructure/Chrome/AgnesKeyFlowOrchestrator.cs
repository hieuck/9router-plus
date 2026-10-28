using RouterPlus.Core.Observability;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Orchestrates Agnes AI API-key acquisition:
/// Google sign-in when needed, then create and copy a new key.
/// </summary>
public static class AgnesKeyFlowOrchestrator
{
    public sealed record AgnesKeyFlowResult(
        bool Success,
        string? ApiKey,
        string? ErrorMessage);

    public static async Task<AgnesKeyFlowResult> RunAsync(
        IAgnesApiKeyBrowser page,
        GoogleLoginCredential credential,
        IGoogleLoginBrowser googleLogin,
        CancellationToken cancellationToken,
        IGoogleAuthenticationService? googleAuthenticationService = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(googleLogin);

        var signedIn = await EnsureSignedInAsync(
            page,
            googleLogin,
            credential,
            cancellationToken,
            googleAuthenticationService,
            popupFactory: null,
            timeout);

        if (signedIn is not null)
        {
            return signedIn;
        }

        return await CreateKeyAsync(page, credential.Email, cancellationToken);
    }

    public static async Task<AgnesKeyFlowResult> RunAsync(
        IAgnesApiKeyBrowser page,
        GoogleLoginCredential credential,
        Func<CancellationToken, Task<IGoogleLoginBrowser>> connectGooglePopup,
        CancellationToken cancellationToken,
        IGoogleAuthenticationService? googleAuthenticationService = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(connectGooglePopup);

        var signedIn = await EnsureSignedInAsync(
            page,
            googleLogin: null,
            credential,
            cancellationToken,
            googleAuthenticationService,
            popupFactory: connectGooglePopup,
            timeout);

        if (signedIn is not null)
        {
            return signedIn;
        }

        return await CreateKeyAsync(page, credential.Email, cancellationToken);
    }

    private static async Task<AgnesKeyFlowResult?> EnsureSignedInAsync(
        IAgnesApiKeyBrowser page,
        IGoogleLoginBrowser? googleLogin,
        GoogleLoginCredential credential,
        CancellationToken cancellationToken,
        IGoogleAuthenticationService? googleAuthenticationService,
        Func<CancellationToken, Task<IGoogleLoginBrowser>>? popupFactory,
        TimeSpan? timeout = null)
    {
        var overallDeadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        AgnesApiKeyPageState? state = null;

        // Phase 1: Poll until we can determine the page state
        while (DateTimeOffset.UtcNow < overallDeadline)
        {
            try
            {
                state = await page.ReadStateAsync(cancellationToken);
                
                // If Google sign-in button is visible, we're on a login page (or keys page showing login)
                // Treat as login page - do not proceed to key creation
                if (state.HasGoogleSignIn)
                {
                    break;
                }
                
                // If on keys page (content-based: has "Create new secret key" button) and no Google button
                // We're signed in - but don't return immediately; let Phase 2 verify
                if (state.IsOnKeysPage && !state.HasGoogleSignIn)
                {
                    break;
                }
                
                // If on login page (sign in/forgot password/password input) without Google button
                // Navigate to /login to find Google button
                if (state.IsOnLoginPage)
                {
                    break;
                }
                
                // If not on keys page and not on login page (e.g., dashboard, about:blank)
                // Navigate to keys page to trigger proper page load
                if (!state.IsOnKeysPage && !state.IsOnLoginPage)
                {
                    await page.NavigateToKeysPageAsync(cancellationToken);
                    await Task.Delay(1000, cancellationToken);
                    continue; // Re-read state after navigation
                }
                
                // About:blank or other page - keep waiting, don't fail on first read
            }
            catch (InvalidOperationException)
            {
                // Execution context may not be ready yet, keep waiting
            }
            
            await Task.Delay(500, cancellationToken);
        }

        if (state == null)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                $"Timed out waiting for Agnes AI page to load. Last known state unavailable.");
        }

        // Phase 2: Handle the detected state
        // If on keys page with content (HasNewKeyButton) and no Google button, we're signed in
        if (state.IsOnKeysPage && !state.HasGoogleSignIn)
        {
            return null;
        }
        
        // If on login page without Google, navigate to /login
        if (state.IsOnLoginPage && !state.HasGoogleSignIn)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AgnesKeyFlow",
                "NavigateToLoginPage",
                "Login page detected without Google button, navigating to /login",
                new { currentUrl = state.PageUri.ToString() });
            
            await page.NavigateToLoginPageAsync(cancellationToken);
            
            // Wait a bit for navigation
            await Task.Delay(1000, cancellationToken);
            
            // Now re-read state
            state = await page.ReadStateAsync(cancellationToken);
        }

        // At this point, we should be on a page with Google button (keys page with Google, or login page with Google)
        // If still no Google button, fail with clear info
        if (!state.HasGoogleSignIn)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                $"Could not find the 'Continue with Google' button. Current URL: {state.PageUri} (IsOnKeysPage: {state.IsOnKeysPage}, IsOnLoginPage: {state.IsOnLoginPage}, HasNewKeyButton: {state.HasNewKeyButton}).");
        }

        // Click Google sign-in button
        var clicked = await page.TryClickSignInWithGoogleAsync(cancellationToken);
        if (!clicked)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                $"Could not click the 'Continue with Google' button. Current URL: {state.PageUri} (IsOnKeysPage: {state.IsOnKeysPage}, IsOnLoginPage: {state.IsOnLoginPage}).");
        }

        // Get the Google login popup (separate CDP target) after click
        IGoogleLoginBrowser? popup = googleLogin;
        bool ownsPopup = false;
        if (popup is null && popupFactory is not null)
        {
            try
            {
                popup = await popupFactory(cancellationToken);
                ownsPopup = true;
            }
            catch (Exception ex)
            {
                return new AgnesKeyFlowResult(
                    false,
                    null,
                    $"Google login popup did not appear: {ex.Message}");
            }
        }

        if (popup is null)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Google login popup did not appear: no popup connector provided.");
        }

        try
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AgnesKeyFlow",
                "GoogleAutoLoginStarted",
                "Running Google autologin",
                new { email = credential.Email });

            var googleAuthentication = googleAuthenticationService ?? new GoogleAuthenticationService();
            var loginResult = await googleAuthentication.AuthenticateAsync(
                new GoogleAuthenticationRequest(credential, popup),
                cancellationToken);

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AgnesKeyFlow",
                "GoogleAutoLoginCompleted",
                "Google login completed",
                new { category = loginResult.Category.ToString(), message = loginResult.Message });

            if (loginResult.Category != GoogleLoginResultCategory.Success)
            {
                return new AgnesKeyFlowResult(
                    false,
                    null,
                    $"Google sign-in failed: {loginResult.Message}");
            }
        }
        finally
        {
            if (ownsPopup)
            {
                await popup.DisposeAsync();
            }
        }

        // Phase 3: After auth, wait for keys page (content-based)
        // Navigate to keys page to ensure we're on the right page
        await page.NavigateToKeysPageAsync(cancellationToken);
        
        // Wait for keys page with generous timeout
        var keysDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        bool onKeysPage = false;
        
        while (DateTimeOffset.UtcNow < keysDeadline)
        {
            try
            {
                state = await page.ReadStateAsync(cancellationToken);
                if (state.IsOnKeysPage)
                {
                    onKeysPage = true;
                    break;
                }
            }
            catch (InvalidOperationException)
            {
                // Context recreated during redirect
            }
            
            await Task.Delay(1000, cancellationToken);
        }

        if (!onKeysPage)
        {
            // Final attempt - try navigating again
            try
            {
                await page.NavigateToKeysPageAsync(cancellationToken);
                await Task.Delay(1000, cancellationToken);
                state = await page.ReadStateAsync(cancellationToken);
                if (state.IsOnKeysPage)
                {
                    return null;
                }
            }
            catch
            {
                // Ignore
            }

            return new AgnesKeyFlowResult(
                false,
                null,
                $"Did not return to Agnes AI keys page after Google sign-in. Final URL: {state?.PageUri}, IsOnKeysPage: {state?.IsOnKeysPage}, IsOnLoginPage: {state?.IsOnLoginPage}, HasGoogleSignIn: {state?.HasGoogleSignIn}, HasNewKeyButton: {state?.HasNewKeyButton}.");
        }

        return null;
    }

    private static async Task<AgnesKeyFlowResult> CreateKeyAsync(
        IAgnesApiKeyBrowser page,
        string profileEmail,
        CancellationToken cancellationToken)
    {
        // Guard: ensure we're on the keys page before trying to click "Create new secret key"
        var state = await page.ReadStateAsync(cancellationToken);
        if (!state.IsOnKeysPage)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                $"Not on the Agnes AI keys page. Current URL: {state.PageUri} (IsOnKeysPage: {state.IsOnKeysPage}, IsOnLoginPage: {state.IsOnLoginPage}, HasGoogleSignIn: {state.HasGoogleSignIn})");
        }

        // Click "Create new secret key"
        var clickedNewKey = await page.TryClickNewKeyAsync(cancellationToken);
        if (!clickedNewKey)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Could not find 'Create new secret key' button.");
        }

        // Wait for the name input modal
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        bool hasNameInput = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var keyState = await page.ReadStateAsync(cancellationToken);
            if (keyState.HasNewKeyNameInput)
            {
                hasNameInput = true;
                break;
            }
            await Task.Delay(500, cancellationToken);
        }

        if (!hasNameInput)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Key name input modal did not appear.");
        }

        // Fill key name and create
        var keyName = $"{profileEmail}-agnes";
        var created = await page.TryCreateKeyAsync(keyName, cancellationToken);
        if (!created)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Failed to create the API key (modal submit failed).");
        }

        // Wait for the key to appear in the disabled input
        deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        string? apiKey = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var keyState = await page.ReadStateAsync(cancellationToken);
            if (keyState.HasCreatedKeyPanel && !string.IsNullOrWhiteSpace(keyState.ApiKey))
            {
                apiKey = keyState.ApiKey;
                break;
            }
            await Task.Delay(500, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "API key was created but could not be read from the modal before it closed.");
        }

        return new AgnesKeyFlowResult(true, apiKey, null);
    }
}