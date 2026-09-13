using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.App.Tests.TestHelpers;

namespace RouterPlus.App.Tests.ViewModels;

public sealed class MainViewModelPublicTests
{
    private static async Task<MainViewModel> CreateViewModelAsync(string directory, params ChromeProfile[] profiles)
    {
        var settingsStore = Mocks.CreateSettingsStore(directory);
        await settingsStore.SaveAsync(new RouterSettings(DashboardBaseUrl: "http://localhost:20128"));

        var viewModel = new MainViewModel(
            settingsStore,
            googleLoginVaultPaths: new GoogleAccountVaultPaths(directory),
            harnessProfiles: profiles);

        await viewModel.InitializeAsync();
        return viewModel;
    }

    [Fact]
    public async Task HasVaultCredentialsAsync_returns_true_when_a_provider_has_credentials()
    {
        var directory = TestData.CreateTempDirectory();
        try
        {
            var profile = TestData.CreateProfile("Work", "Default", directory);
            var vault = Mocks.CreateProviderVault(directory);
            await vault.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = profile.Name,
                Provider = ProviderKind.Codex,
                PreferredMethod = AuthMethod.GoogleOAuth,
                LinkedGoogleAccount = "user@example.com"
            });

            var viewModel = await CreateViewModelAsync(directory, profile);

            Assert.True(await viewModel.HasVaultCredentialsAsync(profile, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HasVaultCredentialsAsync_returns_false_when_no_provider_has_credentials()
    {
        var directory = TestData.CreateTempDirectory();
        try
        {
            var profile = TestData.CreateProfile("Work", "Default", directory);
            var viewModel = await CreateViewModelAsync(directory, profile);

            Assert.False(await viewModel.HasVaultCredentialsAsync(profile, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SelectProfilesWithVaultCredentialsAsync_selects_only_profiles_with_credentials()
    {
        var directory = TestData.CreateTempDirectory();
        try
        {
            var withCreds = TestData.CreateProfile("WithCreds", "Default", directory);
            var withoutCreds = TestData.CreateProfile("WithoutCreds", "Profile 1", directory);
            var vault = Mocks.CreateProviderVault(directory);
            await vault.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = withCreds.Name,
                Provider = ProviderKind.Codex,
                PreferredMethod = AuthMethod.GoogleOAuth,
                LinkedGoogleAccount = "user@example.com"
            });

            var viewModel = await CreateViewModelAsync(directory, withCreds, withoutCreds);

            await viewModel.SelectProfilesWithVaultCredentialsAsync(CancellationToken.None);

            Assert.True(viewModel.IsMultiSelectMode);
            Assert.Equal(1, viewModel.ProfileRows.Count(row => row.IsSelected));
            Assert.Equal(
                "Đã chọn 1 profile có vault credentials",
                viewModel.StatusText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}