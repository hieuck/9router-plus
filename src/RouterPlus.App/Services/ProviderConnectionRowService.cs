using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.App.Services;

public sealed class ProviderConnectionRowService : IProviderConnectionRowService
{
    private readonly IProviderConnectionVaultStore _store;

    public ProviderConnectionRowService(IProviderConnectionVaultStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ProviderConnectionRowData> LoadAsync(
        ChromeProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var codexConnection = await _store.GetConnectionAsync(
            profile.Name,
            ProviderKind.Codex,
            cancellationToken);

        var providerConnections = new List<ProviderConnectionRowItem>(3);
        foreach (var kind in new[] { ProviderKind.Kiro, ProviderKind.GitHub, ProviderKind.OpenRouter })
        {
            var connection = await _store.GetConnectionAsync(
                profile.Name,
                kind,
                cancellationToken);

            providerConnections.Add(new ProviderConnectionRowItem(kind, connection));
        }

        return new ProviderConnectionRowData(profile, codexConnection, providerConnections);
    }
}
