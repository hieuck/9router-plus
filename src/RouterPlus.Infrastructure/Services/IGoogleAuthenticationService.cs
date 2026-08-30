using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Services;

/// <summary>
/// Application-facing boundary for Google credential authentication.
/// Provider flows depend on this service instead of invoking the state machine directly.
/// </summary>
public interface IGoogleAuthenticationService
{
    Task<GoogleLoginResult> AuthenticateAsync(
        GoogleAuthenticationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs for one Google authentication attempt.
/// </summary>
public sealed record GoogleAuthenticationRequest(
    GoogleLoginCredential Credential,
    IGoogleLoginBrowser Browser);
