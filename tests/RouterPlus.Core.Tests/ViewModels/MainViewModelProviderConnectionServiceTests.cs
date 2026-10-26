using RouterPlus.App.Services;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests.ViewModels;

public sealed class MainViewModelProviderConnectionServiceTests
{
    private sealed class FakeProviderConnectionService : IProviderConnectionService
    {
        public ConnectionRefreshResult Result { get; set; } = new(
            Array.Empty<ProviderConnection>(),
            Array.Empty<ProviderConnection>(),
            Array.Empty<ConnectionDisableFailure>(),
            false,
            null);

        public Exception? ThrowOnRefresh { get; set; }

        public Task<ConnectionRefreshResult> RefreshConnectionsAsync(
            Func<Task<IRouterApiClient>> apiFactory,
            CancellationToken cancellationToken)
        {
            if (ThrowOnRefresh is not null)
            {
                throw ThrowOnRefresh;
            }

            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<ProviderConnection>> RefreshConnectionsLightweightAsync(
            Func<Task<IRouterApiClient>> apiFactory,
            CancellationToken cancellationToken)
        {
            if (ThrowOnRefresh is not null)
            {
                throw ThrowOnRefresh;
            }

            return Task.FromResult<IReadOnlyList<ProviderConnection>>(Result.Connections);
        }
    }

    private static ProviderConnection MakeExhaustedConnection(string id) =>
        new(
            id,
            ProviderKind.Codex,
            "Work",
            1,
            true,
            Quotas: new[] { new ProviderQuota("session", 100m, 100m, 0m, DateTimeOffset.UtcNow.AddHours(1)) });

    [Fact]
    public async Task MarkerPersisted_EvenWhenDisabledIdAbsentFromRelistedConnections()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusQuotaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var disabled = MakeExhaustedConnection("codex-1");
            var fake = new FakeProviderConnectionService
            {
                Result = new ConnectionRefreshResult(
                    Array.Empty<ProviderConnection>(),
                    new[] { disabled },
                    Array.Empty<ConnectionDisableFailure>(),
                    false,
                    null)
            };

            var viewModel = new MainViewModel(new SettingsStore(settingsPath), connectionService: fake)
            {
                DashboardBaseUrl = "http://router.test"
            };
            var profile = new ChromeProfile("profile-id", "Work", "Default", Path.Combine(directory, "profile"), true);
            viewModel.Profiles.Add(profile);
            viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
            viewModel.SelectedProfile = profile;
            await viewModel.InitializeQuotaAutoDisableMarkersForTestAsync();

            await viewModel.RefreshConnectionStatusesAsync();

            var marker = Assert.Single(new SettingsStore(settingsPath).Load().QuotaAutoDisableMarkers!);
            Assert.Equal("codex-1", marker.ConnectionId);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task PartialDisableFailures_OnlySuccessfulIdsMarked()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusQuotaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var successful = MakeExhaustedConnection("codex-2");
            var fake = new FakeProviderConnectionService
            {
                Result = new ConnectionRefreshResult(
                    Array.Empty<ProviderConnection>(),
                    new[] { successful },
                    new[] { new ConnectionDisableFailure("codex-1", new InvalidOperationException("boom")) },
                    false,
                    null)
            };

            var viewModel = new MainViewModel(new SettingsStore(settingsPath), connectionService: fake)
            {
                DashboardBaseUrl = "http://router.test"
            };
            var profile = new ChromeProfile("profile-id", "Work", "Default", Path.Combine(directory, "profile"), true);
            viewModel.Profiles.Add(profile);
            viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
            viewModel.SelectedProfile = profile;
            await viewModel.InitializeQuotaAutoDisableMarkersForTestAsync();

            await viewModel.RefreshConnectionStatusesAsync();

            var markers = new SettingsStore(settingsPath).Load().QuotaAutoDisableMarkers!;
            Assert.Single(markers);
            Assert.Equal("codex-2", markers[0].ConnectionId);
            Assert.Contains("thất bại 1 connection", viewModel.ConnectionStatusText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Cancellation_DoesNotSaveMarkers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusQuotaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var fake = new FakeProviderConnectionService
            {
                ThrowOnRefresh = new OperationCanceledException()
            };

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var viewModel = new MainViewModel(new SettingsStore(settingsPath), connectionService: fake)
            {
                DashboardBaseUrl = "http://router.test"
            };
            var profile = new ChromeProfile("profile-id", "Work", "Default", Path.Combine(directory, "profile"), true);
            viewModel.Profiles.Add(profile);
            viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
            viewModel.SelectedProfile = profile;
            await viewModel.InitializeQuotaAutoDisableMarkersForTestAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => viewModel.RefreshConnectionStatusesAsync(cancellation.Token));

            Assert.Null(new SettingsStore(settingsPath).Load().QuotaAutoDisableMarkers);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
