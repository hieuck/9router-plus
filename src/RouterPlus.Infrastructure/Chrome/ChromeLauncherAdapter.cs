using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Adapter that implements IChromeLauncher for AutoLoginOrchestrator.
/// Bridges between concrete ChromeLauncher and the interface required by orchestrator.
///
/// Phase 6 Step 6.1 - Created for AutoLoginOrchestrator integration
/// </summary>
public sealed class ChromeLauncherAdapter : IChromeLauncher
{
    private readonly ChromeLauncher _chromeLauncher;
    private readonly ChromeInstallation _installation;
    private readonly ChromeProfile _profile;
    private ChromeManagedSession? _currentSession;

    public ChromeLauncherAdapter(
        ChromeLauncher chromeLauncher,
        ChromeInstallation installation,
        ChromeProfile profile)
    {
        _chromeLauncher = chromeLauncher ?? throw new ArgumentNullException(nameof(chromeLauncher));
        _installation = installation ?? throw new ArgumentNullException(nameof(installation));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public async Task<CdpSession?> LaunchAsync(
        string profileName,
        Uri loginUrl,
        CancellationToken cancellationToken)
    {
        // Launch Chrome with managed session
        _currentSession = await _chromeLauncher.LaunchManagedAsync(
            _installation,
            _profile,
            loginUrl,
            cancellationToken,
            useOriginalProfile: true);

        // Connect to CDP
        var cdpSession = await _currentSession.ConnectAnyTargetAsync(cancellationToken);

        return cdpSession;
    }

    /// <summary>
    /// Cleanup the current Chrome session.
    /// Should be called after auto-login completes.
    /// </summary>
    public async Task CleanupAsync()
    {
        if (_currentSession != null)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                await _currentSession.DisposeAsync();
                _currentSession = null;
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
