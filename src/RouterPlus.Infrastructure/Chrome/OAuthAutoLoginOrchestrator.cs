using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Orchestrates the full OAuth auto-login flow: launch Chrome with the user's
/// profile, navigate to the auth URL, and auto-click account picker / consent.
/// Falls back gracefully so the caller can still detect new connections via proxy.
/// </summary>
public sealed class OAuthAutoLoginOrchestrator : IAsyncDisposable
{
    private readonly ChromeManagedSession _session;
    private readonly CdpSession _cdpSession;

    private bool _disposed;

    public OAuthAutoLoginOrchestrator(ChromeManagedSession session, CdpSession cdpSession)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _cdpSession = cdpSession ?? throw new ArgumentNullException(nameof(cdpSession));
    }

    /// <summary>
    /// Navigates the connected Chrome page to <paramref name="authUrl"/> and runs
    /// consent automation until the user lands on the target service or timeout fires.
    /// </summary>
    public async Task<OAuthAutoLoginResult> RunAsync(
        Uri authUrl,
        Uri targetServiceUri,
        string profileEmail,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authUrl);
        ArgumentNullException.ThrowIfNull(targetServiceUri);
        ArgumentNullException.ThrowIfNull(profileEmail);

        DebugConsole.WriteLine($"[OAuthAutoLogin] Navigating to auth URL: {authUrl}");
        System.Diagnostics.Debug.WriteLine($"[OAuthAutoLogin] FULL URL: {authUrl}");
        await ChromeManagedSession.NavigateAsync(_cdpSession, authUrl, cancellationToken);

        // Give Chrome a moment to render the initial page before polling
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        var automation = new CodexOAuthAutomation(_cdpSession.Client, _cdpSession.SessionId, _cdpSession.TargetId, profileEmail);
        var consent = await automation.WaitAndConsentAsync(targetServiceUri, timeout, cancellationToken);

        if (consent.Success)
        {
            DebugConsole.WriteLine($"[OAuthAutoLogin] Success: {consent.Message}");
            return new OAuthAutoLoginResult(
                OAuthAutoLoginOutcome.Success,
                consent.Message,
                AlreadyAuthorized: consent.AlreadyAuthorized);
        }

        DebugConsole.WriteLine($"[OAuthAutoLogin] Failed: {consent.Message}");
        return new OAuthAutoLoginResult(
            OAuthAutoLoginOutcome.ConsentFailed,
            consent.Message,
            AlreadyAuthorized: false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _cdpSession.DisposeAsync();
        }
        catch
        {
            // Best effort
        }
    }
}

/// <summary>
/// Outcome categories for OAuth auto-login. Callers map this to user-facing messages.
/// </summary>
public enum OAuthAutoLoginOutcome
{
    Success,
    ConsentFailed,
    BrowserError,
    Cancelled,
}

/// <summary>
/// Result of an OAuth auto-login attempt.
/// </summary>
public sealed record OAuthAutoLoginResult(
    OAuthAutoLoginOutcome Outcome,
    string Message,
    bool AlreadyAuthorized);