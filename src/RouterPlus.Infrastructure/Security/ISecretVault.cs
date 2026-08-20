namespace RouterPlus.Infrastructure.Security;

public interface ISecretVault
{
    Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default);

    Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
