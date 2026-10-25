using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;

namespace RouterPlus.Infrastructure.Security;

/// <summary>
/// Storage interface for encrypted provider connection vault.
/// Mirrors <see cref="ProviderConnectionVaultStore"/> public methods.
/// </summary>
public interface IProviderConnectionVaultStore
{
    Task<ProviderAuthConnection?> GetConnectionAsync(
        string profileName,
        ProviderKind provider,
        CancellationToken cancellationToken = default);

    Task SaveConnectionAsync(
        ProviderAuthConnection connection,
        CancellationToken cancellationToken = default);

    Task RemoveConnectionAsync(
        string profileName,
        ProviderKind provider,
        CancellationToken cancellationToken = default);

    Task<bool> HasCredentialsAsync(
        string profileName,
        ProviderKind provider,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<ProviderKind, ProviderAuthConnection>> GetProfileConnectionsAsync(
        string profileName,
        CancellationToken cancellationToken = default);
}
