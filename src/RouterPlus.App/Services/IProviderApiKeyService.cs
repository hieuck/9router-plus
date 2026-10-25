using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.App.Services;

public sealed record AddApiKeyResult(
    bool Created,
    ProviderConnection Connection,
    ProviderConnectionTestResult TestResult);

public interface IProviderApiKeyService
{
    Task<AddApiKeyResult> AddOrUpdateAsync(
        ProviderKind provider,
        string apiKey,
        ChromeProfile profile,
        Func<Task<IRouterApiClient>> apiFactory,
        CancellationToken cancellationToken);
}