namespace RouterPlus.Infrastructure.Chrome;

public sealed record PoolsideApiKeyPageState(
    Uri PageUri,
    bool IsOnKeysPage,
    bool HasGoogleSignIn,
    int ExistingKeyCount,
    bool HasNewKeyButton,
    bool HasNewKeyNameInput,
    bool HasCreatedKeyPanel,
    string ApiKey);

public interface IPoolsideApiKeyBrowser : IAsyncDisposable
{
    Task<PoolsideApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken);

    Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken);

    Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken);

    Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken);

    Task<bool> TryDeleteOneExistingKeyAsync(CancellationToken cancellationToken);

    Task<bool> TryClickNewKeyAsync(CancellationToken cancellationToken);

    Task<bool> TryCreateKeyAsync(string name, CancellationToken cancellationToken);
}
