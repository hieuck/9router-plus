namespace RouterPlus.Infrastructure.Chrome;

public sealed record AgnesApiKeyPageState(
    Uri PageUri,
    bool IsOnKeysPage,
    bool IsOnLoginPage,
    bool HasGoogleSignIn,
    int ExistingKeyCount,
    bool HasNewKeyButton,
    bool HasNewKeyNameInput,
    bool HasCreatedKeyPanel,
    string ApiKey,
    IReadOnlyList<ButtonCandidate>? Candidates = null);

public sealed record ButtonCandidate(
    string Role,
    string Tag,
    string Text,
    string AriaLabel,
    string ClassName);

public interface IAgnesApiKeyBrowser : IAsyncDisposable
{
    Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken);

    Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null);

    Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken);

    Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null);

    Task<bool> TryDeleteOneExistingKeyAsync(CancellationToken cancellationToken);

    Task<bool> TryClickNewKeyAsync(CancellationToken cancellationToken);

    Task<bool> TryCreateKeyAsync(string name, CancellationToken cancellationToken);

    Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken);
}