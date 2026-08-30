using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Provider-specific OAuth flow boundary. Google credential authentication is supplied
/// by the shared authentication service rather than implemented by an adapter.
/// </summary>
public interface IProviderOAuthAdapter
{
    ProviderKind Provider { get; }

    Task<ProviderOAuthResult> RunAsync(
        ProviderOAuthRequest request,
        IGoogleAuthenticationService googleAuthentication,
        CancellationToken cancellationToken);
}
