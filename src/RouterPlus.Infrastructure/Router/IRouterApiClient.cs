using RouterPlus.Core.Providers;

namespace RouterPlus.Infrastructure.Router;

public interface IRouterApiClient
{
    Task<IReadOnlyList<ProviderConnection>> ListAllConnectionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderConnection>> ListConnectionsAsync(
        ProviderKind provider,
        CancellationToken cancellationToken = default);

    Task<ProviderConnectionTestResult> TestConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    Task<OAuthAuthorizationSession> StartOAuthAuthorizationAsync(
        ProviderKind provider,
        string redirectUri,
        CancellationToken cancellationToken = default);

    Task<OAuthProxyStartResult> StartOAuthProxyAsync(
        ProviderKind provider,
        int appPort,
        OAuthAuthorizationSession session,
        CancellationToken cancellationToken = default);

    Task<OAuthProxyStatus> GetOAuthProxyStatusAsync(
        ProviderKind provider,
        string state,
        CancellationToken cancellationToken = default);

    Task ExchangeOAuthCodeAsync(
        ProviderKind provider,
        string code,
        string redirectUri,
        string? codeVerifier,
        string? state,
        CancellationToken cancellationToken = default);

    Task<DeviceCodeSession> StartDeviceCodeAsync(
        ProviderKind provider,
        string? authMethod = null,
        CancellationToken cancellationToken = default);

    Task<DeviceCodePollResult> PollDeviceCodeAsync(
        ProviderKind provider,
        DeviceCodeSession session,
        CancellationToken cancellationToken = default);

    Task<ProviderConnection> AddApiKeyConnectionAsync(
        ProviderKind provider,
        string name,
        string apiKey,
        int priority,
        CancellationToken cancellationToken = default);

    Task UpdateConnectionAsync(
        string connectionId,
        string? name = null,
        int? priority = null,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    Task<ProviderConnection> WaitForNewConnectionAsync(
        ProviderKind provider,
        IReadOnlySet<string> existingConnectionIds,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default);
}
