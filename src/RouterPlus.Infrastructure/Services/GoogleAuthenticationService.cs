using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Services;

/// <summary>
/// Shared Google authentication foundation backed by the existing bounded state machine.
/// </summary>
public sealed class GoogleAuthenticationService : IGoogleAuthenticationService
{
    public Task<GoogleLoginResult> AuthenticateAsync(
        GoogleAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return GoogleLoginStateMachine.RunAsync(
            request.Browser,
            request.Credential,
            cancellationToken);
    }
}
