using RouterPlus.App.ViewModels;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelProfileContextMenuTests
{
    [Fact]
    public async Task DeleteSelectedProfile_removes_directory_mapping_and_selects_remaining_profile()
    {
        var directory = CreateTempDirectory();
        var userDataDirectory = Path.Combine(directory, "User Data");
        var chromeExecutablePath = Path.Combine(directory, "chrome.exe");
        var settingsPath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(Path.Combine(userDataDirectory, "Default"));
        Directory.CreateDirectory(Path.Combine(userDataDirectory, "Profile 1"));
        File.WriteAllText(Path.Combine(userDataDirectory, "Local State"), """
            {
              "profile": {
                "info_cache": {
                  "Default": { "name": "Personal" },
                  "Profile 1": { "name": "Work" }
                }
              }
            }
            """);
        File.WriteAllText(chromeExecutablePath, string.Empty);
        await new SettingsStore(settingsPath).SaveAsync(new RouterSettings(
            DashboardBaseUrl: "http://127.0.0.1:1",
            ChromeExecutablePath: chromeExecutablePath,
            ChromeUserDataDirectory: userDataDirectory,
            ManagedProfiles:
            [
                new("Work", "Profile 1", userDataDirectory)
            ]));

        try
        {
            var viewModel = new MainViewModel(new SettingsStore(settingsPath));
            await viewModel.InitializeAsync();
            viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Name == "Work");

            await viewModel.DeleteSelectedProfileAsync();

            Assert.False(Directory.Exists(Path.Combine(userDataDirectory, "Profile 1")));
            var remainingProfile = Assert.Single(viewModel.Profiles);
            Assert.Equal("Personal", remainingProfile.Name);
            Assert.Equal(remainingProfile, viewModel.SelectedProfile);

            var settings = await new SettingsStore(settingsPath).LoadAsync();
            Assert.Empty(settings.ManagedProfiles!);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task OpenSelectedGoogleLogin_without_profile_reports_selection_status()
    {
        var viewModel = new MainViewModel();

        await viewModel.OpenSelectedGoogleLoginAsync();

        Assert.Equal("Hãy chọn Chrome profile trước.", viewModel.StatusText);
    }

    [Fact]
    public void SelectProfileForContextMenu_selects_profile_immediately()
    {
        var viewModel = new MainViewModel();
        var profile = new RouterPlus.Core.Chrome.ChromeProfile(
            RouterPlus.Core.Chrome.ChromeProfile.CreateId("C:\\Chrome\\User Data", "Profile 1"),
            "Work",
            "Profile 1",
            "C:\\Chrome\\User Data",
            false);

        viewModel.SelectProfileForContextMenu(profile);

        Assert.Equal(profile, viewModel.SelectedProfile);
    }

    [Fact]
    public void CreateGoogleAutoLoginViewModel_returns_view_model_for_selected_profile()
    {
        var fakeVaultStore = new FakeVaultStore();
        var fakeAutomation = new Func<RouterPlus.Core.Chrome.ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>(
            (profile, credential, cancellationToken) => Task.FromResult(GoogleLoginResult.Success()));
        var viewModel = new MainViewModel(googleLoginVaultStore: fakeVaultStore, googleLoginAutomation: fakeAutomation);
        var profile = new RouterPlus.Core.Chrome.ChromeProfile(
            RouterPlus.Core.Chrome.ChromeProfile.CreateId("C:\\Chrome\\User Data", "Profile 1"),
            "Work",
            "Profile 1",
            "C:\\Chrome\\User Data",
            false);
        viewModel.SelectedProfile = profile;

        var dialogViewModel = viewModel.CreateGoogleAutoLoginViewModel();

        Assert.NotNull(dialogViewModel);
        Assert.Equal("Profile 1", dialogViewModel.ProfileName);
    }

    [Fact]
    public void CreateGoogleAutoLoginViewModel_returns_null_when_no_profile_selected()
    {
        var fakeVaultStore = new FakeVaultStore();
        var viewModel = new MainViewModel(googleLoginVaultStore: fakeVaultStore);

        var dialogViewModel = viewModel.CreateGoogleAutoLoginViewModel();

        Assert.Null(dialogViewModel);
        Assert.Equal("Hãy chọn Chrome profile trước.", viewModel.StatusText);
    }

    [Fact]
    public async Task GoogleLoginAutomation_manual_intervention_does_not_dispose_while_other_results_do()
    {
        var fakeVaultStore = new FakeVaultStore();
        var disposalTracker = new DisposalTracker();
        var manualAutomation = CreateAutomationWithDisposalTracking(disposalTracker, GoogleLoginResult.ManualInterventionRequired("CAPTCHA required"));
        var successAutomation = CreateAutomationWithDisposalTracking(disposalTracker, GoogleLoginResult.Success());
        var profile = new RouterPlus.Core.Chrome.ChromeProfile(
            RouterPlus.Core.Chrome.ChromeProfile.CreateId("C:\\Chrome\\User Data", "Profile 1"),
            "Work",
            "Profile 1",
            "C:\\Chrome\\User Data",
            false);
        var credential = new GoogleLoginCredential("profile-1", "user@example.com", "password", "JBSWY3DPEHPK3PXP");

        // Manual intervention: should NOT dispose
        disposalTracker.Reset();
        var manualResult = await manualAutomation(profile, credential, CancellationToken.None);
        Assert.Equal(GoogleLoginResultCategory.ManualInterventionRequired, manualResult.Category);
        Assert.False(disposalTracker.BrowserDisposed, "Browser should remain open for manual intervention");
        Assert.False(disposalTracker.SessionDisposed, "Session should remain open for manual intervention");

        // Success: should dispose
        disposalTracker.Reset();
        var successResult = await successAutomation(profile, credential, CancellationToken.None);
        Assert.Equal(GoogleLoginResultCategory.Success, successResult.Category);
        Assert.True(disposalTracker.BrowserDisposed, "Browser should be disposed on success");
        Assert.True(disposalTracker.SessionDisposed, "Session should be disposed on success");
    }

    private static Func<RouterPlus.Core.Chrome.ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> CreateAutomationWithDisposalTracking(
        DisposalTracker tracker,
        GoogleLoginResult result)
    {
        return async (profile, credential, cancellationToken) =>
        {
            var fakeBrowser = new FakeDisposableBrowser(tracker);
            var fakeSession = new FakeDisposableSession(tracker);

            // Simulate the automation logic
            if (result.Category == GoogleLoginResultCategory.ManualInterventionRequired)
            {
                // Do not dispose on manual intervention
                return result;
            }

            // Dispose on other outcomes
            await fakeBrowser.DisposeAsync();
            await fakeSession.DisposeAsync();
            return result;
        };
    }

    private sealed class DisposalTracker
    {
        public bool BrowserDisposed { get; set; }
        public bool SessionDisposed { get; set; }

        public void Reset()
        {
            BrowserDisposed = false;
            SessionDisposed = false;
        }
    }

    private sealed class FakeDisposableBrowser : IAsyncDisposable
    {
        private readonly DisposalTracker _tracker;

        public FakeDisposableBrowser(DisposalTracker tracker)
        {
            _tracker = tracker;
        }

        public ValueTask DisposeAsync()
        {
            _tracker.BrowserDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDisposableSession : IAsyncDisposable
    {
        private readonly DisposalTracker _tracker;

        public FakeDisposableSession(DisposalTracker tracker)
        {
            _tracker = tracker;
        }

        public ValueTask DisposeAsync()
        {
            _tracker.SessionDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeVaultStore : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default)
        {
            var session = new FakeSession();
            return Task.FromResult<GoogleAccountVaultSession>(session);
        }

        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default)
        {
            var session = new FakeSession();
            return Task.FromResult<GoogleAccountVaultSession>(session);
        }

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<GoogleAccountVaultSession?>(null);
        }

        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        private sealed class FakeSession : GoogleAccountVaultSession
        {
            public string VaultId => "test-vault-id";
            public GoogleAccountVault Vault => new GoogleAccountVault();

            public void Replace(GoogleAccountVault vault)
            {
            }

            public Task RememberAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task RemoveRememberedAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
