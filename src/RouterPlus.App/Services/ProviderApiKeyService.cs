using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.App.Services;

public sealed class ProviderApiKeyService : IProviderApiKeyService
{
    private readonly ISecretVault _secretVault;

    public ProviderApiKeyService(ISecretVault? secretVault = null)
    {
        _secretVault = secretVault ?? new DpapiSecretVault();
    }

    public async Task<AddApiKeyResult> AddOrUpdateAsync(
        ProviderKind provider,
        string apiKey,
        ChromeProfile profile,
        Func<Task<IRouterApiClient>> apiFactory,
        CancellationToken cancellationToken)
    {
        if (apiFactory is null)
        {
            throw new ArgumentNullException(nameof(apiFactory));
        }

        var api = await apiFactory();
        var existing = await api.ListConnectionsAsync(provider, cancellationToken, includeQuotas: false);
        var existingConnection = existing.FirstOrDefault(connection =>
            string.Equals(connection.Name?.Trim(), profile.Name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (existingConnection is not null)
        {
            await api.UpdateConnectionAsync(
                existingConnection.Id,
                name: profile.Name,
                apiKey: apiKey,
                cancellationToken: cancellationToken);
            await _secretVault.StoreAsync(
                ProfileSecretKey.Create(profile, provider),
                apiKey,
                cancellationToken);
            var testResult = await api.TestConnectionAsync(existingConnection.Id, cancellationToken);
            return new AddApiKeyResult(false, existingConnection, testResult);
        }

        var priority = PriorityCalculator.Next(existing);
        var created = await api.AddApiKeyConnectionAsync(
            provider,
            profile.Name,
            apiKey,
            priority,
            cancellationToken);
        await _secretVault.StoreAsync(
            ProfileSecretKey.Create(profile, provider),
            apiKey,
            cancellationToken);
        var createdTestResult = await api.TestConnectionAsync(created.Id, cancellationToken);
        return new AddApiKeyResult(true, created, createdTestResult);
    }
}