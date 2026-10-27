namespace RouterPlus.Infrastructure.Chrome;

public sealed record AgnesApiKeyPageState(
    Uri PageUri,
    bool IsOnKeysPage,
    bool HasGoogleSignIn,
    int ExistingKeyCount,
    bool HasNewKeyButton,
    bool HasNewKeyNameInput,
    bool HasCreatedKeyPanel,
    string ApiKey);

public interface IAgnesApiKeyBrowser : IAsyncDisposable
{
    Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken);

    Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken);

    Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken);

    Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken);

    Task<bool> TryDeleteOneExistingKeyAsync(CancellationToken cancellationToken);

    Task<bool> TryClickNewKeyAsync(CancellationToken cancellationToken);

    Task<bool> TryCreateKeyAsync(string name, CancellationToken cancellationToken);
}