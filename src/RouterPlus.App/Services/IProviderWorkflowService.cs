using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.App.Services;

public interface IProviderWorkflowService
{
    bool IsInProgress { get; }
    ProviderKind? CurrentProvider { get; }
    CancellationTokenSource? WorkflowCancellation { get; }
    Dictionary<string, ProviderConnection> WorkflowExistingConnections { get; }
    DateTimeOffset WorkflowStartedAt { get; }

    void Cancel();

    Task<bool> ConnectOpenRouterOAuthAsync(CancellationToken cancellationToken = default);
    Task OpenProviderAsync(ProviderKind provider);
    Task OpenProviderForTestAsync(ProviderKind provider);
    Task RunOAuthWorkflowAsync(IRouterApiClient api, ProviderKind provider, CancellationToken cancellationToken);
    Task RunDeviceCodeWorkflowAsync(IRouterApiClient api, ProviderKind provider, CancellationToken cancellationToken);
    Task RunDeviceCodeWithAutomationAsync(IRouterApiClient api, ProviderKind provider, string verificationUri, DeviceCodeSession session, CancellationToken cancellationToken);
    Task PollDeviceCodeUntilSuccessAsync(IRouterApiClient api, ProviderKind provider, DeviceCodeSession session, CancellationToken cancellationToken);
    Task CaptureExistingConnectionsAsync(IRouterApiClient api, ProviderKind provider, CancellationToken cancellationToken = default);
    Task WaitForOAuthProxyAsync(IRouterApiClient api, ProviderKind provider, string state, TimeSpan timeout, CancellationToken cancellationToken);
    Task RenameNewConnectionAsync(IRouterApiClient api, ProviderKind provider, CancellationToken cancellationToken = default);
    Task WaitForConnectionAsync();
    Task RunOAuthAutoLoginAsync(ProviderKind provider, string authUrl, Uri targetServiceUri, CancellationToken cancellationToken);
}
