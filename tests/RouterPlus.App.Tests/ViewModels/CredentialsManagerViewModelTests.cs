using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Security;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.App.ViewModels;
using RouterPlus.App.Services;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// Behavior tests for the Credentials Manager vault and per-row login workflows.
/// </summary>
public sealed partial class CredentialsManagerViewModelTests : IAsyncLifetime
{
    private readonly string _rootDirectory;
    private readonly GoogleAccountVaultPaths _vaultPaths;
    private readonly GoogleAccountVaultStore _googleVaultStore;
    private readonly ProviderConnectionVaultStore _providerVaultStore;
    private readonly ChromeProfile _profile;
    private readonly MainViewModel _mainViewModel;
    private readonly List<CredentialsManagerViewModel> _viewModels = new();
    private readonly List<ProviderConnectionVaultStore> _syntheticProviderStores = new();
    private readonly List<GoogleAccountVaultSession> _syntheticSessions = new();

    public CredentialsManagerViewModelTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"RouterPlus-CredentialsTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);

        _vaultPaths = new GoogleAccountVaultPaths(_rootDirectory);
        _googleVaultStore = new GoogleAccountVaultStore(_vaultPaths);
        _providerVaultStore = new ProviderConnectionVaultStore(
            Path.Combine(_rootDirectory, "provider-connections.vault"));
        _profile = new ChromeProfile(
            ChromeProfile.CreateId(_rootDirectory, "Default"),
            "Test Profile",
            "Default",
            _rootDirectory,
            true);
        _mainViewModel = new MainViewModel(
            settingsStore: new SettingsStore(Path.Combine(_rootDirectory, "settings.json")), // temp settings store: InitializeAsync runs the one-time DPAPI migration
            googleLoginVaultPaths: _vaultPaths,
            harnessProfiles: new[] { _profile });
    }

    public async Task InitializeAsync()
    {
        await _mainViewModel.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var viewModel in _viewModels)
        {
            await viewModel.DisposeAsync();
        }

        foreach (var session in _syntheticSessions)
        {
            await session.DisposeAsync();
        }

        foreach (var store in _syntheticProviderStores)
        {
            store.Dispose();
        }

        _providerVaultStore.Dispose();
        _googleVaultStore.Dispose();

        if (Directory.Exists(_rootDirectory))
        {
            try
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup for files still held by the test process.
            }
        }

        await Task.CompletedTask;
    }




























































    // ── SaveRowAsync guards ──









    // ── CheckHealthRowAsync guards ──






    // ── CheckAllHealthAsync ──



    // ── SaveCodexRowAsync guards ──








    // ── LoginCodexRowAsync guards ──











    // ── SaveProviderRowAsync guards ──










    // ── LoginProviderRowAsync guards ──







    // ── RemoveGoogleAccountAsync guards ──





    // ── RemoveCodexConnectionAsync guards ──


    // ── RemoveProviderConnectionAsync exception ──


    // ── RemoveCodexConnection exception ──


    // ── ToggleSelectAllCodex ──



    // ── Batch login edge cases ──









    // ── Properties ──











    // ── CodexConnectionRowViewModel property tests ──

















    // ── SaveRowAsync with save exception via ThrowingVaultStore ──




    private static GoogleLoginResult CreateGoogleLoginResult(
        GoogleLoginResultCategory category,
        string message)
    {
        return category switch
        {
            GoogleLoginResultCategory.Success => GoogleLoginResult.Success(),
            GoogleLoginResultCategory.InvalidCredentials => GoogleLoginResult.InvalidCredentials(),
            GoogleLoginResultCategory.ManualInterventionRequired => GoogleLoginResult.ManualInterventionRequired(message),
            GoogleLoginResultCategory.Timeout => GoogleLoginResult.Timeout(),
            GoogleLoginResultCategory.Cancelled => GoogleLoginResult.Cancelled(),
            GoogleLoginResultCategory.BrowserDisconnected => GoogleLoginResult.BrowserDisconnected(message),
            GoogleLoginResultCategory.UnsupportedPage => GoogleLoginResult.UnsupportedPage(message),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };
    }

    private CredentialsManagerViewModel CreateViewModel(
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>? automation = null,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>? healthCheck = null,
        Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>>? codexAuthentication = null,
        IGoogleAccountVaultStore? googleVaultStore = null,
        ICredentialsProviderLoginService? credentialsProviderLoginService = null)
    {
        var viewModel = new CredentialsManagerViewModel(
            _mainViewModel,
            googleVaultStore ?? _googleVaultStore,
            _providerVaultStore,
            _vaultPaths,
            automation ?? ((_, _, _) => Task.FromResult(GoogleLoginResult.Success())),
            healthCheck ?? ((_, _, _) => Task.FromResult(GoogleLoginResult.Success())),
            codexAuthentication ?? ((_, _, _) => Task.FromResult(CodexLoginResult.Success())),
            credentialsProviderLoginService: credentialsProviderLoginService);
        _viewModels.Add(viewModel);
        return viewModel;
    }

    private static Exception CreateVaultOpenException(string errorKind) => errorKind switch
    {
        "argument" => new ArgumentException("synthetic secret: invalid password"),
        "unauthorized" => new UnauthorizedAccessException("synthetic secret: denied"),
        "invalid-operation" => new InvalidOperationException("synthetic vault state failure"),
        _ => new Exception("synthetic secret: unexpected failure")
    };

    private sealed class SaveThrowingGoogleVaultStore(IGoogleAccountVaultStore inner) : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(path, vaultPassword, cancellationToken);
        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            inner.OpenAsync(path, vaultPassword, cancellationToken);
        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) =>
            inner.TryOpenRememberedAsync(path, cancellationToken);
        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault save failure");
        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) =>
            inner.ExportAsync(session, destinationPath, exportPassword, cancellationToken);
        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) =>
            inner.ImportAsync(currentPath, sourcePath, sourcePassword, cancellationToken);
    }

    private sealed class FakeCredentialsProviderLoginService(
        Func<ProviderKind, ChromeProfile, ProviderCredential, Func<Task<string?>>?, CancellationToken, Task<CredentialsProviderLoginResult>> handler) : ICredentialsProviderLoginService
    {
        public int CallCount { get; private set; }
        public Task<CredentialsProviderLoginResult> LoginAsync(
            ProviderKind provider,
            ChromeProfile profile,
            ProviderCredential credential,
            Func<Task<string?>>? totpGenerator = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return handler(provider, profile, credential, totpGenerator, cancellationToken);
        }
    }

    private sealed class ThrowingGoogleVaultStore(Exception exception) : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) => Task.FromException<GoogleAccountVaultSession>(exception);
        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) => Task.FromException<GoogleAccountVaultSession>(exception);
        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<GoogleAccountVaultSession?>(null);
        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) => Task.FromException(exception);
        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) => Task.FromException(exception);
        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) => Task.FromException(exception);
    }

    private CredentialsManagerViewModel CreateSyntheticViewModel(
        GoogleLoginCredential? credential = null,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>? healthCheck = null)
    {
        var providerStore = new ProviderConnectionVaultStore(
            Path.Combine(_rootDirectory, $"provider-{Guid.NewGuid():N}.vault"));
        var session = new SyntheticVaultSession(
            credential is null
                ? new GoogleAccountVault()
                : new GoogleAccountVault(new[] { credential }));
        var viewModel = new CredentialsManagerViewModel(
            _mainViewModel,
            new SyntheticGoogleVaultStore(session),
            providerStore,
            _vaultPaths,
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            healthCheck ?? ((_, _, _) => Task.FromResult(GoogleLoginResult.Success())),
            (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
        _syntheticProviderStores.Add(providerStore);
        _syntheticSessions.Add(session);
        _viewModels.Add(viewModel);
        return viewModel;
    }

    private async Task CreateVaultAsync(string password, params GoogleLoginCredential?[] credentials)
    {
        await using var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            password,
            CancellationToken.None);
        var records = credentials.OfType<GoogleLoginCredential>().ToArray();
        if (records.Length > 0)
        {
            session.Replace(new GoogleAccountVault(records));
        }

        await _googleVaultStore.SaveAsync(session, CancellationToken.None);
    }

    private static async Task<GoogleLoginResult> WaitForCancellationAsync(
        CancellationToken cancellationToken,
        TaskCompletionSource<bool> cancelled)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled.TrySetResult(true);
            return GoogleLoginResult.Cancelled();
        }

        return GoogleLoginResult.Success();
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(predicate(), "The expected asynchronous state was not reached within five seconds.");
    }

    private sealed class SyntheticGoogleVaultStore(GoogleAccountVaultSession session) : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession?>(session);

        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SyntheticVaultSession(GoogleAccountVault vault) : GoogleAccountVaultSession
    {
        public string VaultId => "synthetic-vault";
        public GoogleAccountVault Vault { get; private set; } = vault;

        public void Replace(GoogleAccountVault vault) => Vault = vault;
        public Task RememberAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveRememberedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AlwaysThrowGoogleVaultStore : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault create failure");

        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault open failure");

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault try open failure");

        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault save failure");

        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault export failure");

        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault import failure");
    }
}
