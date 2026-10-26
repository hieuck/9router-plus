using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.App.Services;

public sealed record ProviderConnectionRenameResult(
    ProviderConnection Connection,
    bool FoundAfterTimeout,
    bool ReusedExistingId);

public interface IProviderConnectionRenameService
{
    Task<ProviderConnectionRenameResult> RenameAsync(
        IRouterApiClient api,
        ProviderKind provider,
        ChromeProfile profile,
        IReadOnlyDictionary<string, ProviderConnection> existingConnections,
        DateTimeOffset workflowStartedAt,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}