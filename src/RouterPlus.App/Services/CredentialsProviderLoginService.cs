using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.App.Services;

/// <summary>
/// Credentials Manager provider-row direct login. Reuses
/// <see cref="IDirectLoginAutomationFactory"/> and the managed Chrome/CDP
/// path; provider-specific automation stays in the infrastructure layer.
/// </summary>
public sealed class CredentialsProviderLoginService : ICredentialsProviderLoginService
{
    private readonly IDirectLoginAutomationFactory _directLoginFactory;
    private readonly Func<ChromeProfile, Uri, CancellationToken, Task<IProviderLoginChromeSession>> _launchChrome;
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(2);

    public CredentialsProviderLoginService(
        Func<ChromeProfile, Uri, CancellationToken, Task<IProviderLoginChromeSession>> launchChrome,
        IDirectLoginAutomationFactory? directLoginFactory = null)
    {
        _launchChrome = launchChrome ?? throw new ArgumentNullException(nameof(launchChrome));
        _directLoginFactory = directLoginFactory ?? new DefaultDirectLoginAutomationFactory();
    }

    public CredentialsProviderLoginService(
        IDirectLoginAutomationFactory directLoginFactory,
        Func<ChromeProfile, Uri, CancellationToken, Task<IProviderLoginChromeSession>> launchChrome)
        : this(launchChrome, directLoginFactory)
    {
    }

    public async Task<CredentialsProviderLoginResult> LoginAsync(
        ProviderKind provider,
        ChromeProfile profile,
        ProviderCredential credential,
        Func<Task<string?>>? totpGenerator = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(credential);

        var loginUrl = ProviderCatalog.GetDirectLoginStartUri(provider);
        if (loginUrl is null)
        {
            return new CredentialsProviderLoginResult(false, $"Direct login not supported for provider {provider}.");
        }

        IProviderLoginChromeSession? session = null;
        try
        {
            session = await _launchChrome(profile, loginUrl, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                return new CredentialsProviderLoginResult(false, "Failed to launch browser.");
            }

            CdpSession cdpSession;
            try
            {
                cdpSession = await session.ConnectAnyTargetAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                throw;
            }

            try
            {
                IDirectLoginAutomation automation;
                try
                {
                    automation = _directLoginFactory.Create(provider, cdpSession, credential, totpGenerator);
                }
                catch (NotSupportedException ex)
                {
                    return new CredentialsProviderLoginResult(false, ex.Message);
                }

                var result = await automation.RunAsync(LoginTimeout, cancellationToken).ConfigureAwait(false);
                return new CredentialsProviderLoginResult(
                    result.Success,
                    result.Success ? "Login completed successfully." : (string.IsNullOrWhiteSpace(result.Message) ? "Login failed." : result.Message));
            }
            finally
            {
                await cdpSession.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return new CredentialsProviderLoginResult(false, "Login automation was cancelled.");
        }
        catch (Exception ex)
        {
            return new CredentialsProviderLoginResult(false, ex.Message);
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }
    }
}