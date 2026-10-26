using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RouterPlus.App.Services;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests.ViewModels;

public sealed class MainViewModelProviderConnectionServiceTests
{
    private static ProviderConnection MakeConnection(
        string id,
        ProviderKind provider,
        string? name,
        int priority = 1,
        bool isActive = true,
        string? email = null) =>
        new ProviderConnection(id, provider, name, priority, isActive, email);

    private static ProviderConnection MakeExhaustedConnection(string id) =>
        new(
            id,
            ProviderKind.Codex,
            "Work",
            1,
            true,
            Quotas: new[] { new ProviderQuota("session", 100m, 100m, 0m, DateTimeOffset.UtcNow.AddHours(1)) });

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

    // ----- MainViewModel Refresh Tests -----

    [Fact]
    public async Task Refresh_UnmatchedConnectionCount_AppearsInConnectionStatusText_WhenGreaterThanZero()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusRefreshTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var profile = new ChromeProfile("profile-1", "Work", "Default", Path.Combine(directory, "profile"), true);
            
            // Connection that matches profile
            var matchedConnection = MakeConnection("matched-1", ProviderKind.Codex, "Work", 1);
            // Connection that does NOT match any profile
            var unmatchedConnection = MakeConnection("unmatched-1", ProviderKind.Codex, "NonExistentProfile", 1);
            
            var fake = new FakeProviderConnectionService
            {
                Result = new ConnectionRefreshResult(
                    new[] { matchedConnection, unmatchedConnection },
                    Array.Empty<ProviderConnection>(),
                    Array.Empty<ConnectionDisableFailure>(),
                    false,
                    null)
            };

            var viewModel = new MainViewModel(new SettingsStore(settingsPath), connectionService: fake)
            {
                DashboardBaseUrl = "http://router.test"
            };
            viewModel.Profiles.Add(profile);
            viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
            viewModel.SelectedProfile = profile;
            await viewModel.InitializeQuotaAutoDisableMarkersForTestAsync();

            // Act
            await viewModel.RefreshConnectionStatusesAsync();

            // Assert
            Assert.Contains("1 connection chưa khớp profile", viewModel.ConnectionStatusText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Refresh_AllConnectionsMatched_KeepsNormalStatus_NoUnmatchedSuffix()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusRefreshTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var profile = new ChromeProfile("profile-1", "Work", "Default", Path.Combine(directory, "profile"), true);
            
            // All connections match the profile
            var matchedConnection = MakeConnection("matched-1", ProviderKind.Codex, "Work", 1);
            var matchedConnection2 = MakeConnection("matched-2", ProviderKind.OpenRouter, "Work", 1);
            
            var fake = new FakeProviderConnectionService
            {
                Result = new ConnectionRefreshResult(
                    new[] { matchedConnection, matchedConnection2 },
                    Array.Empty<ProviderConnection>(),
                    Array.Empty<ConnectionDisableFailure>(),
                    false,
                    null)
            };

            var viewModel = new MainViewModel(new SettingsStore(settingsPath), connectionService: fake)
            {
                DashboardBaseUrl = "http://router.test"
            };
            viewModel.Profiles.Add(profile);
            viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
            viewModel.SelectedProfile = profile;
            await viewModel.InitializeQuotaAutoDisableMarkersForTestAsync();

            // Act
            await viewModel.RefreshConnectionStatusesAsync();

            // Assert
            Assert.DoesNotContain("connection chưa khớp profile", viewModel.ConnectionStatusText, StringComparison.Ordinal);
            Assert.Contains("2 connection", viewModel.ConnectionStatusText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Refresh_NoFalseAssignment_UnmatchedConnectionNotAssignedToAnyProfile()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusRefreshTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var profile = new ChromeProfile("profile-1", "Work", "Default", Path.Combine(directory, "profile"), true);
            
            // Connection that does NOT match the profile name
            var unmatchedConnection = MakeConnection("unmatched-1", ProviderKind.Codex, "NonExistentProfile", 1);
            
            var fake = new FakeProviderConnectionService
            {
                Result = new ConnectionRefreshResult(
                    new[] { unmatchedConnection },
                    Array.Empty<ProviderConnection>(),
                    Array.Empty<ConnectionDisableFailure>(),
                    false,
                    null)
            };

            var viewModel = new MainViewModel(new SettingsStore(settingsPath), connectionService: fake)
            {
                DashboardBaseUrl = "http://router.test"
            };
            viewModel.Profiles.Add(profile);
            var row = new ProfileRowViewModel(profile, viewModel.Providers);
            viewModel.ProfileRows.Add(row);
            viewModel.SelectedProfile = profile;
            await viewModel.InitializeQuotaAutoDisableMarkersForTestAsync();

            // Act
            await viewModel.RefreshConnectionStatusesAsync();

            // Assert - the profile should have 0 connections (not falsely assigned)
            var codexStatus = row.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
            Assert.Empty(codexStatus.Connections);
            Assert.Equal(0, row.ConnectedProviderCount);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Refresh_MultipleProfiles_UnmatchedCountCorrect()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusRefreshTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var profile1 = new ChromeProfile("profile-1", "Work", "Default", Path.Combine(directory, "profile1"), true);
            var profile2 = new ChromeProfile("profile-2", "Personal", "Default", Path.Combine(directory, "profile2"), true);
            
            // 1 matched for Work, 1 matched for Personal, 2 unmatched
            var matchedWork = MakeConnection("matched-work", ProviderKind.Codex, "Work", 1);
            var matchedPersonal = MakeConnection("matched-personal", ProviderKind.Codex, "Personal", 1);
            var unmatched1 = MakeConnection("unmatched-1", ProviderKind.Codex, "Ghost1", 1);
            var unmatched2 = MakeConnection("unmatched-2", ProviderKind.OpenRouter, "Ghost2", 1);
            
            var fake = new FakeProviderConnectionService
            {
                Result = new ConnectionRefreshResult(
                    new[] { matchedWork, matchedPersonal, unmatched1, unmatched2 },
                    Array.Empty<ProviderConnection>(),
                    Array.Empty<ConnectionDisableFailure>(),
                    false,
                    null)
            };

            var viewModel = new MainViewModel(new SettingsStore(settingsPath), connectionService: fake)
            {
                DashboardBaseUrl = "http://router.test"
            };
            viewModel.Profiles.Add(profile1);
            viewModel.Profiles.Add(profile2);
            viewModel.ProfileRows.Add(new ProfileRowViewModel(profile1, viewModel.Providers));
            viewModel.ProfileRows.Add(new ProfileRowViewModel(profile2, viewModel.Providers));
            viewModel.SelectedProfile = profile1;
            await viewModel.InitializeQuotaAutoDisableMarkersForTestAsync();

            // Act
            await viewModel.RefreshConnectionStatusesAsync();

            // Assert
            Assert.Contains("2 connection chưa khớp profile", viewModel.ConnectionStatusText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}