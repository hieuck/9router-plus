using RouterPlus.App;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Updates;
using RouterPlus.Infrastructure.Updates;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelUpdateTests
{
    [Fact]
    public void About_view_model_contains_only_public_application_metadata()
    {
        var about = new AboutViewModel();

        Assert.Equal("9Router Profile Tool", about.ProductName);
        Assert.NotEqual("0.0.0", about.Version);
        Assert.Equal("MIT License", about.LicenseName);
        Assert.Equal("github.com", about.RepositoryUri.Host);
        Assert.Equal("github.com", about.HelpUri.Host);
        Assert.DoesNotContain("@", about.ProductName, StringComparison.Ordinal);
        Assert.DoesNotContain("@", about.Version, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_command_opens_only_the_fixed_public_help_link()
    {
        var launcher = new RecordingLinkLauncher();
        var viewModel = new MainViewModel(updateService: new FakeUpdateService(), linkLauncher: launcher);

        await viewModel.OpenHelpAsync();

        Assert.Single(launcher.OpenedUris);
        Assert.Equal("https", launcher.OpenedUris[0].Scheme);
        Assert.Equal("github.com", launcher.OpenedUris[0].Host);
        Assert.Equal("/hieuck/9router-plus/blob/master/docs/user-guide.md", launcher.OpenedUris[0].AbsolutePath);
        Assert.DoesNotContain("profile", launcher.OpenedUris[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", launcher.OpenedUris[0].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void External_link_allowlist_rejects_a_lookalike_repository_path()
    {
        var lookalike = new Uri("https://github.com/hieuck/9router-plus.evil");

        Assert.False(ApplicationLinks.IsAllowed(lookalike));
    }

    [Fact]
    public async Task Check_for_updates_reports_no_update_without_exposing_response_data()
    {
        var service = new FakeUpdateService
        {
            CheckResult = new ReleaseCheckResult(ReleaseVersion.Parse("1.0.0"), null, null, null, null, null)
        };
        var viewModel = new MainViewModel(updateService: service);

        await viewModel.CheckForUpdatesAsync();

        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Equal(UpdateState.Idle, viewModel.UpdateState);
        Assert.Contains("mới nhất", viewModel.UpdateStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profile", viewModel.UpdateStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", viewModel.UpdateStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_for_updates_reports_available_stable_version()
    {
        var service = new FakeUpdateService { CheckResult = CreateAvailableResult() };
        var viewModel = new MainViewModel(updateService: service);

        await viewModel.CheckForUpdatesAsync();

        Assert.True(viewModel.IsUpdateAvailable);
        Assert.Equal("1.1.0", viewModel.AvailableVersion?.ToString());
        Assert.Equal(UpdateState.Available, viewModel.UpdateState);
        Assert.True(viewModel.InstallUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Check_for_updates_fails_closed_without_logging_exception_details()
    {
        var service = new FakeUpdateService { Error = new InvalidOperationException("secret-profile-email@example.com") };
        var viewModel = new MainViewModel(updateService: service);

        await viewModel.CheckForUpdatesAsync();

        Assert.Equal(UpdateState.Failed, viewModel.UpdateState);
        Assert.Contains("không thể kiểm tra", viewModel.UpdateStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-profile-email@example.com", viewModel.UpdateStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Installation_is_disabled_when_signature_verification_is_unavailable()
    {
        var service = new FakeUpdateService { IsInstallSupported = false, CheckResult = CreateAvailableResult() };
        var viewModel = new MainViewModel(updateService: service);

        await viewModel.CheckForUpdatesAsync();

        Assert.Equal(UpdateState.Disabled, viewModel.UpdateState);
        Assert.False(viewModel.InstallUpdateCommand.CanExecute(null));
        Assert.Contains("chữ ký", viewModel.UpdateStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Installation_requires_explicit_confirmation_before_launching_updater()
    {
        var service = new FakeUpdateService { CheckResult = CreateAvailableResult() };
        var viewModel = new MainViewModel(updateService: service);
        await viewModel.CheckForUpdatesAsync();

        var startedWithoutConfirmation = await viewModel.InstallUpdateAsync(confirmedByUser: false);

        Assert.False(startedWithoutConfirmation);
        Assert.False(service.DownloadCalled);
        Assert.False(service.LaunchCalled);
    }

    private static ReleaseCheckResult CreateAvailableResult() => new(
        ReleaseVersion.Parse("1.0.0"),
        ReleaseVersion.Parse("1.1.0"),
        "safe release notes",
        new ReleaseAsset("RouterPlus-v1.1.0-win-x64.zip", new Uri("https://github.com/hieuck/9router-plus/releases/download/v1.1.0/RouterPlus-v1.1.0-win-x64.zip"), 10, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true),
        new ReleaseAsset("RouterPlus-v1.1.0-win-x64.zip.sha256", new Uri("https://github.com/hieuck/9router-plus/releases/download/v1.1.0/RouterPlus-v1.1.0-win-x64.zip.sha256"), 64, null, true),
        new ReleaseAsset("RouterPlus-v1.1.0-manifest.json", new Uri("https://github.com/hieuck/9router-plus/releases/download/v1.1.0/RouterPlus-v1.1.0-manifest.json"), 512, null, true));

    private sealed class FakeUpdateService : IUpdateService
    {
        public bool IsInstallSupported { get; init; } = true;
        public ReleaseCheckResult? CheckResult { get; init; }
        public Exception? Error { get; init; }
        public bool DownloadCalled { get; private set; }
        public bool LaunchCalled { get; private set; }

        public Task<ReleaseCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            if (Error is not null) throw Error;
            return Task.FromResult(CheckResult ?? new ReleaseCheckResult(ReleaseVersion.Parse("1.0.0"), null, null, null, null, null));
        }

        public Task<VerifiedUpdatePackage> DownloadAndStageAsync(ReleaseCheckResult release, CancellationToken cancellationToken = default)
        {
            DownloadCalled = true;
            throw new InvalidOperationException("test service should not be called without confirmation");
        }

        public Task<bool> LaunchUpdaterAsync(VerifiedUpdatePackage package, CancellationToken cancellationToken = default)
        {
            LaunchCalled = true;
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingLinkLauncher : IExternalLinkLauncher
    {
        public List<Uri> OpenedUris { get; } = [];
        public void Open(Uri uri) => OpenedUris.Add(uri);
    }
}
