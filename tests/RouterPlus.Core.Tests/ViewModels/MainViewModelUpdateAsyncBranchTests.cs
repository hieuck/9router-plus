using RouterPlus.App.ViewModels;
using RouterPlus.Core.Updates;
using RouterPlus.Infrastructure.Updates;

namespace RouterPlus.Core.Tests.ViewModels;

public sealed class MainViewModelUpdateAsyncBranchTests
{
    [Fact]
    public async Task InstallUpdateAsync_when_staging_and_launch_succeed_reports_completed()
    {
        // Arrange
        var service = new FakeUpdateService
        {
            CheckResult = AvailableRelease(),
            Package = new VerifiedUpdatePackage(ReleaseVersion.Parse("1.1.0"), "archive.zip", "staging"),
            LaunchResult = true
        };
        var viewModel = new MainViewModel(updateService: service);
        await viewModel.CheckForUpdatesAsync();

        // Act
        var installed = await viewModel.InstallUpdateAsync(confirmedByUser: true);

        // Assert
        Assert.True(installed);
        Assert.True(service.DownloadCalled);
        Assert.True(service.LaunchCalled);
        Assert.Equal(UpdateState.Completed, viewModel.UpdateState);
    }

    [Fact]
    public async Task InstallUpdateAsync_when_updater_launch_fails_preserves_running_version()
    {
        // Arrange
        var service = new FakeUpdateService
        {
            CheckResult = AvailableRelease(),
            Package = new VerifiedUpdatePackage(ReleaseVersion.Parse("1.1.0"), "archive.zip", "staging"),
            LaunchResult = false
        };
        var viewModel = new MainViewModel(updateService: service);
        await viewModel.CheckForUpdatesAsync();

        // Act
        var installed = await viewModel.InstallUpdateAsync(confirmedByUser: true);

        // Assert
        Assert.False(installed);
        Assert.Equal(UpdateState.Failed, viewModel.UpdateState);
        Assert.Contains("khởi động trình cập nhật", viewModel.UpdateStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallUpdateAsync_when_download_is_cancelled_reports_failed_without_leaking_exception()
    {
        // Arrange
        var service = new FakeUpdateService
        {
            CheckResult = AvailableRelease(),
            DownloadException = new OperationCanceledException()
        };
        var viewModel = new MainViewModel(updateService: service);
        await viewModel.CheckForUpdatesAsync();

        // Act
        var installed = await viewModel.InstallUpdateAsync(confirmedByUser: true);

        // Assert
        Assert.False(installed);
        Assert.Equal(UpdateState.Failed, viewModel.UpdateState);
        Assert.Contains("Đã hủy cập nhật", viewModel.UpdateStatusText, StringComparison.Ordinal);
        Assert.False(service.LaunchCalled);
    }

    private static ReleaseCheckResult AvailableRelease() => new(
        ReleaseVersion.Parse("1.0.0"),
        ReleaseVersion.Parse("1.1.0"),
        null,
        new ReleaseAsset("archive.zip", new Uri("https://github.com/hieuck/9router-plus/releases/download/v1.1.0/archive.zip"), 1, null, true),
        new ReleaseAsset("archive.zip.sha256", new Uri("https://github.com/hieuck/9router-plus/releases/download/v1.1.0/archive.zip.sha256"), 1, null, true));

    private sealed class FakeUpdateService : IUpdateService
    {
        public bool IsInstallSupported => true;
        public ReleaseCheckResult? CheckResult { get; init; }
        public VerifiedUpdatePackage? Package { get; init; }
        public bool LaunchResult { get; init; }
        public Exception? DownloadException { get; init; }
        public bool DownloadCalled { get; private set; }
        public bool LaunchCalled { get; private set; }

        public Task<ReleaseCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CheckResult!);

        public Task<VerifiedUpdatePackage> DownloadAndStageAsync(ReleaseCheckResult release, CancellationToken cancellationToken = default)
        {
            DownloadCalled = true;
            if (DownloadException is not null)
            {
                throw DownloadException;
            }

            return Task.FromResult(Package!);
        }

        public Task<bool> LaunchUpdaterAsync(VerifiedUpdatePackage package, CancellationToken cancellationToken = default)
        {
            LaunchCalled = true;
            return Task.FromResult(LaunchResult);
        }
    }
}
