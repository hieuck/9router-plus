using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.App.Services;

/// <summary>
/// Testable seam over a managed Chrome session used by provider-row direct login.
/// </summary>
public interface IProviderLoginChromeSession : IAsyncDisposable
{
    Task<CdpSession> ConnectAnyTargetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Adapter around <see cref="ChromeManagedSession"/> for the seam above.
/// </summary>
internal sealed class ProviderLoginChromeSession : IProviderLoginChromeSession
{
    private readonly ChromeManagedSession _session;

    public ProviderLoginChromeSession(ChromeManagedSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public Task<CdpSession> ConnectAnyTargetAsync(CancellationToken cancellationToken) =>
        _session.ConnectAnyTargetAsync(cancellationToken);

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}