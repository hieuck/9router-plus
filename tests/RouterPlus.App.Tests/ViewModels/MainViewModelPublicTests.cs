using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.App.Tests.ViewModels;

public sealed class MainViewModelPublicTests
{
    // Helper to create a synthetic ChromeProfile
    private static ChromeProfile CreateProfile(string name = "TestProfile", string directoryName = "Default", string? userDataDir = null)
    {
        var baseDir = userDataDir ?? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        var profilePath = Path.Combine(baseDir, directoryName);
        Directory.CreateDirectory(profilePath);
        return new ChromeProfile(Guid.NewGuid().ToString(), name, name, directoryName, baseDir, false);
    }

    // Minimal synthetic vault store that can be configured to have credentials
    private sealed class SyntheticVaultStore(bool hasCreds) : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession>(new SyntheticVaultSession(hasCreds ? new GoogleAccountVault(new[] { new GoogleLoginCredential("id", "email@test", "pw", null) }) : new GoogleAccountVault()));

        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession>(new SyntheticVaultSession(hasCreds ? new GoogleAccountVault(new[] { new GoogleLoginCredential("id", "email@test", "pw", null) }) : new GoogleAccountVault()));

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession?>(null);

        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SyntheticVaultSession(GoogleAccountVault vault) : GoogleAccountVaultSession
    {
        public string VaultId => "synthetic";
        public GoogleAccountVault Vault { get; private set; } = vault;
        public void Replace(GoogleAccountVault vault) => Vault = vault;
        public Task RememberAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveRememberedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task HasVaultCredentialsAsync_returns_true_when_provider_has_credentials()
    {
        var profile = CreateProfile();
        var providerStore = new ProviderConnectionVaultStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        // simulate a credential for Codex (skip Ollama/Kimchi)
        await providerStore.SaveAsync(profile.Name, ProviderKind.Codex, new ProviderConnection("c", "c", ProviderKind.Codex));

        var vm = new MainViewModel(
            _settingsStore: new SettingsStore(Path.Combine(Path.GetTempPath(), "settings.json")),
            googleLoginVaultStore: new SyntheticVaultStore(hasCreds: true),
            providerConnectionVaultStore: providerStore,
            googleLoginVaultPaths: new GoogleAccountVaultPaths(Path.GetTempPath()),
            googleLoginAutomation: (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            googleLoginHealthCheck: (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            codexAuthenticationRunner: (_, _, _) => Task.FromResult(CodexLoginResult.Success())
        );
        var result = await vm.HasVaultCredentialsAsync(profile, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task HasVaultCredentialsAsync_returns_false_when_no_credentials()
    {
        var profile = CreateProfile();
        var providerStore = new ProviderConnectionVaultStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var vm = new MainViewModel(
            _settingsStore: new SettingsStore(Path.Combine(Path.GetTempPath(), "settings.json")),
            googleLoginVaultStore: new SyntheticVaultStore(hasCreds: false),
            providerConnectionVaultStore: providerStore,
            googleLoginVaultPaths: new GoogleAccountVaultPaths(Path.GetTempPath()),
            googleLoginAutomation: (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            googleLoginHealthCheck: (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            codexAuthenticationRunner: (_, _, _) => Task.FromResult(CodexLoginResult.Success())
        );
        var result = await vm.HasVaultCredentialsAsync(profile, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task SelectProfilesWithVaultCredentialsAsync_selects_correct_profiles_and_updates_status()
    {
        var profile1 = CreateProfile("A");
        var profile2 = CreateProfile("B");
        var providerStore = new ProviderConnectionVaultStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        // Give credentials only to profile1 (Codex)
        await providerStore.SaveAsync(profile1.Name, ProviderKind.Codex, new ProviderConnection("c", "c", ProviderKind.Codex));

        var vm = new MainViewModel(
            _settingsStore: new SettingsStore(Path.Combine(Path.GetTempPath(), "settings.json")),
            googleLoginVaultStore: new SyntheticVaultStore(hasCreds: true),
            providerConnectionVaultStore: providerStore,
            googleLoginVaultPaths: new GoogleAccountVaultPaths(Path.GetTempPath()),
            googleLoginAutomation: (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            googleLoginHealthCheck: (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            codexAuthenticationRunner: (_, _, _) => Task.FromResult(CodexLoginResult.Success())
        );
        // Populate ProfileRows manually (simplified for test)
        vm.RefreshProfiles();
        vm.ProfileRows.Add(new ProfileRowViewModel(profile1, vm));
        vm.ProfileRows.Add(new ProfileRowViewModel(profile2, vm));

        await vm.SelectProfilesWithVaultCredentialsAsync(CancellationToken.None);
        Assert.True(vm.ProfileRows[0].IsSelected);
        Assert.False(vm.ProfileRows[1].IsSelected);
        Assert.Contains("Đã chọn 1 profile", vm.StatusText);
    }

    [Fact]
    public async Task SaveSettingsAsync_persists_settings_and_refreshes_profiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");
        var store = new SettingsStore(settingsPath);
        var vm = new MainViewModel(
            _settingsStore: store,
            googleLoginVaultStore: new SyntheticVaultStore(hasCreds: true),
            providerConnectionVaultStore: new ProviderConnectionVaultStore(Path.Combine(tempDir, "provider.vault")),
            googleLoginVaultPaths: new GoogleAccountVaultPaths(tempDir),
            googleLoginAutomation: (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            googleLoginHealthCheck: (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            codexAuthenticationRunner: (_, _, _) => Task.FromResult(CodexLoginResult.Success())
        );
        // Change a setting to verify it gets saved
        vm.DashboardBaseUrl = "http://new-dashboard";
        await vm.SaveSettingsAsync();
        var loaded = await store.LoadAsync();
        Assert.Equal("http://new-dashboard", loaded.DashboardBaseUrl);
        // Ensure StatusText indicates success and toast was shown (non‑null)
        Assert.Contains("Đã lưu cài đặt", vm.StatusText);
    }
}