using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using RouterPlus.Core.Updates;
using RouterPlus.Infrastructure.Updates;

namespace RouterPlus.Core.Tests;

public sealed class SelfUpdateServiceTests
{
    [Fact]
    public async Task Download_and_stage_verifies_a_valid_release_package()
    {
        var archiveBytes = CreateUpdateArchive();
        var checksum = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
        var handler = new AssetHandler(archiveBytes, $"{checksum}  RouterPlus-v1.1.0-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var updateRoot = CreateUpdateRoot();
        var service = new SelfUpdateService(httpClient, ReleaseVersion.Parse("1.0.0"), updateRoot: updateRoot);

        try
        {
            var package = await service.DownloadAndStageAsync(CreateAvailableResult());

            Assert.Equal("1.1.0", package.Version.ToString());
            Assert.True(File.Exists(Path.Combine(package.StagingPath, "RouterPlus.exe")));
            Assert.True(File.Exists(Path.Combine(package.StagingPath, "RouterPlus.Updater.exe")));
        }
        finally
        {
            DeleteUpdateRoot(updateRoot);
        }
    }

    [Fact]
    public async Task Download_and_stage_cleans_partial_download_when_checksum_request_fails()
    {
        var handler = new AssetHandler(Array.Empty<byte>(), string.Empty)
        {
            ThrowOnChecksumRequest = true
        };
        using var httpClient = new HttpClient(handler);
        var updateRoot = CreateUpdateRoot();
        var service = new SelfUpdateService(httpClient, ReleaseVersion.Parse("1.0.0"), updateRoot: updateRoot);

        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.DownloadAndStageAsync(CreateAvailableResult()));

            Assert.False(Directory.Exists(UpdatePaths.ResolveUnderRoot(updateRoot, "1.1.0")));
        }
        finally
        {
            DeleteUpdateRoot(updateRoot);
        }
    }

    [Fact]
    public async Task Download_and_stage_rejects_release_without_verified_assets_before_download()
    {
        using var httpClient = new HttpClient(new ThrowingHandler());
        var service = new SelfUpdateService(httpClient, ReleaseVersion.Parse("1.0.0"));
        var release = new ReleaseCheckResult(ReleaseVersion.Parse("1.0.0"), null, null, null, null);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(release));
    }

    [Fact]
    public async Task Download_and_stage_rejects_unapproved_asset_before_download()
    {
        using var httpClient = new HttpClient(new ThrowingHandler());
        var updateRoot = CreateUpdateRoot();
        var service = new SelfUpdateService(httpClient, ReleaseVersion.Parse("1.0.0"), updateRoot: updateRoot);
        var release = CreateAvailableResult() with
        {
            Archive = CreateAvailableResult().Archive! with
            {
                DownloadUri = new Uri("http://github.com/hieuck/9router-plus/releases/download/v1.1.0/RouterPlus-v1.1.0-win-x64.zip")
            }
        };

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(release));
            Assert.False(Directory.Exists(UpdatePaths.ResolveUnderRoot(updateRoot, "1.1.0")));
        }
        finally
        {
            DeleteUpdateRoot(updateRoot);
        }
    }

    [Fact]
    public async Task Windows_updater_launcher_returns_false_when_updater_file_is_missing()
    {
        var launcher = new WindowsUpdaterProcessLauncher();

        var launched = await launcher.LaunchAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "RouterPlus.Updater.exe"),
            Path.GetTempPath(),
            Path.GetTempPath(),
            Path.GetTempPath(),
            Environment.ProcessId,
            ReleaseVersion.Parse("1.1.0"));

        Assert.False(launched);
    }

    [Fact]
    public async Task Launch_updater_delegates_paths_and_version_to_process_launcher()
    {
        var root = Path.Combine(Path.GetTempPath(), "RouterPlusUpdateTests", Guid.NewGuid().ToString("N"));
        var stagingPath = Path.Combine(root, "1.1.0", "staging");
        Directory.CreateDirectory(stagingPath);
        var launcher = new RecordingUpdaterLauncher();
        using var httpClient = new HttpClient(new ThrowingHandler());
        var service = new SelfUpdateService(httpClient, ReleaseVersion.Parse("1.0.0"), launcher);
        var package = new VerifiedUpdatePackage(ReleaseVersion.Parse("1.1.0"), Path.Combine(root, "archive.zip"), stagingPath);

        try
        {
            var launched = await service.LaunchUpdaterAsync(package);

            Assert.True(launched);
            Assert.Equal(Path.Combine(stagingPath, "RouterPlus.Updater.exe"), launcher.UpdaterPath);
            Assert.Equal(Path.Combine(root, "1.1.0", "backup"), launcher.BackupDirectory);
            Assert.Equal(package.Version, launcher.Version);
            Assert.Equal(Environment.ProcessId, launcher.ProcessId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ReleaseCheckResult CreateAvailableResult() => new(
        ReleaseVersion.Parse("1.0.0"),
        ReleaseVersion.Parse("1.1.0"),
        "notes",
        new ReleaseAsset(
            "RouterPlus-v1.1.0-win-x64.zip",
            new Uri("https://github.com/hieuck/9router-plus/releases/download/v1.1.0/RouterPlus-v1.1.0-win-x64.zip"),
            0,
            null,
            true),
        new ReleaseAsset(
            "RouterPlus-v1.1.0-win-x64.zip.sha256",
            new Uri("https://github.com/hieuck/9router-plus/releases/download/v1.1.0/RouterPlus-v1.1.0-win-x64.zip.sha256"),
            0,
            null,
            true));

    private static byte[] CreateUpdateArchive()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var fileName in new[] { "RouterPlus.exe", "RouterPlus.Updater.exe" })
            {
                using var entryStream = archive.CreateEntry(fileName).Open();
                entryStream.Write(Encoding.UTF8.GetBytes(fileName));
            }
        }

        return stream.ToArray();
    }

    private static string CreateUpdateRoot() =>
        Path.Combine(Path.GetTempPath(), "RouterPlusUpdateTests", Guid.NewGuid().ToString("N"));

    private static void DeleteUpdateRoot(string updateRoot)
    {
        if (Directory.Exists(updateRoot))
        {
            Directory.Delete(updateRoot, recursive: true);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP should not be called");
    }

    private sealed class RecordingUpdaterLauncher : IUpdaterProcessLauncher
    {
        public string? UpdaterPath { get; private set; }
        public string? BackupDirectory { get; private set; }
        public int ProcessId { get; private set; }
        public ReleaseVersion? Version { get; private set; }

        public Task<bool> LaunchAsync(
            string updaterPath,
            string targetDirectory,
            string stagingDirectory,
            string backupDirectory,
            int processId,
            ReleaseVersion version,
            CancellationToken cancellationToken = default)
        {
            UpdaterPath = updaterPath;
            BackupDirectory = backupDirectory;
            ProcessId = processId;
            Version = version;
            return Task.FromResult(true);
        }
    }

    private sealed class AssetHandler(byte[] archiveBytes, string checksumText) : HttpMessageHandler
    {
        public bool ThrowOnChecksumRequest { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (ThrowOnChecksumRequest)
                {
                    throw new HttpRequestException("synthetic checksum download failure");
                }

                return Task.FromResult(CreateResponse(checksumText, "text/plain"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archiveBytes)
            });
        }

        private static HttpResponseMessage CreateResponse(string content, string mediaType) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, mediaType)
            };
    }
}
