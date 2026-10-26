using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;

namespace RouterPlus.App.Services;

/// <summary>
/// Small result of a provider direct-login attempt initiated from the
/// Credentials Manager provider rows.
/// </summary>
public sealed record CredentialsProviderLoginResult(bool Success, string Message);

/// <summary>
/// Runs direct (email/password/TOTP) login for a provider row using the
/// existing provider automation, without duplicating browser automation.
/// </summary>
public interface ICredentialsProviderLoginService
{
    Task<CredentialsProviderLoginResult> LoginAsync(
        ProviderKind provider,
        ChromeProfile profile,
        ProviderCredential credential,
        Func<Task<string?>>? totpGenerator = null,
        CancellationToken cancellationToken = default);
}