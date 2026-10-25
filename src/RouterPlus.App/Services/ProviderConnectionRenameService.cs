using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.App.Services;

public sealed class ProviderConnectionRenameService : IProviderConnectionRenameService
{
    public async Task<ProviderConnectionRenameResult> RenameAsync(
        IRouterApiClient api,
        ProviderKind provider,
        ChromeProfile profile,
        IReadOnlyDictionary<string, ProviderConnection> existingConnections,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (api is null)
        {
            throw new ArgumentNullException(nameof(api));
        }
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }
        if (existingConnections is null)
        {
            throw new ArgumentNullException(nameof(existingConnections));
        }

        var foundAfterTimeout = false;
        var reusedExistingId = false;
        ProviderConnection connection;

        try
        {
            connection = await api.WaitForNewConnectionAsync(
                provider,
                existingConnections,
                timeout,
                TimeSpan.FromSeconds(2),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            var all = await api.ListConnectionsAsync(provider, cancellationToken, includeQuotas: false);
            var match = all
                .Where(c => string.Equals(c.Name, profile.Name, StringComparison.OrdinalIgnoreCase))
                .Where(c => !existingConnections.ContainsKey(c.Id))
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefault();

            if (match is null)
            {
                match = all
                    .Where(c => string.Equals(c.Name, profile.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(c => c.CreatedAt)
                    .FirstOrDefault();
            }

            if (match is null)
            {
                throw;
            }

            connection = match;
            foundAfterTimeout = true;
            reusedExistingId = existingConnections.ContainsKey(connection.Id);
        }

        await api.UpdateConnectionAsync(
            connection.Id,
            name: profile.Name,
            cancellationToken: cancellationToken);

        return new ProviderConnectionRenameResult(connection, foundAfterTimeout, reusedExistingId);
    }
}