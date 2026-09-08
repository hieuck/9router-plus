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
    public async Task Download_and_stage_rejects_an_unverified_release_without_downloading()
    {
        var handler = new AssetHandler(Array.Empty<byte>(), string.Empty);
        using var httpClient = new HttpClient(handler);
        var updateRoot = CreateUpdateRoot();
        var service = new SelfUpdateService(httpClient, ReleaseVersion.Parse("1.0.0"), updateRoot: updateRoot);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(
                new ReleaseCheckResult(ReleaseVersion.Parse("1.0.0"), null, null, null, null)));

            Assert.Empty(handler.RequestedUris);
        }
        finally
        {
            DeleteUpdateRoot(updateRoot);
        }
    }

    [Fact]
    public async Task Download_and_stage_rejects_an_asset_with_an_untrusted_uri_and_cleans_stage()
    {
        var handler = new AssetHandler(Array.Empty<byte>(), string.Empty);
        using var httpClient = new HttpClient(handler);
        var updateRoot = CreateUpdateRoot();
        var service = new SelfUpdateService(httpClient, ReleaseVersion.Parse("1.0.0"), updateRoot: updateRoot);
        var release = CreateAvailableResult() with
        {
            Archive = new ReleaseAsset(
                "RouterPlus-v1.1.0-win-x64.zip",
                new Uri("http://github.com/hieuck/9router-plus/releases/download/v1.1.0/RouterPlus-v1.1.0-win-x64.zip"),
                0,
                null,
                true)
        };

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(release));
            Assert.False(Directory.Exists(UpdatePaths.VersionRoot(updateRoot, ReleaseVersion.Parse("1.1.0"))));
            Assert.Empty(handler.RequestedUris);
        }
        finally
        {
            DeleteUpdateRoot(updateRoot);
        }
    }

    [Fact]
    public async Task Download_and_stage_rejects_a_response_redirect_to_an_untrusted_host()
    {
        var archiveBytes = CreateUpdateArchive();
        var checksum = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
        var handler = new AssetHandler(archiveBytes, $"{checksum}  RouterPlus-v1.1.0-win-x64.zip")
        {
            ResponseUri = new Uri("https://example.test/asset")
        };
        using var httpClient = new HttpClient(handler);
        var updateRoot = CreateUpdateRoot();
        var service = new SelfUpdateService(httpClient, ReleaseVersion.Parse("1.0.0"), updateRoot: updateRoot);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(CreateAvailableResult()));
            Assert.False(Directory.Exists(UpdatePaths.VersionRoot(updateRoot, ReleaseVersion.Parse("1.1.0"))));
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

            Assert.False(Directory.Exists(UpdatePaths.VersionRoot(updateRoot, ReleaseVersion.Parse("1.1.0"))));
        }
        finally
        {
            DeleteUpdateRoot(updateRoot);
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

    private static string CreateUpdateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteUpdateRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AssetHandler(byte[] archiveBytes, string checksumText) : HttpMessageHandler
    {
        public bool ThrowOnChecksumRequest { get; init; }

        public Uri? ResponseUri { get; init; }

        public List<Uri> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);
            if (request.RequestUri?.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (ThrowOnChecksumRequest)
                {
                    throw new HttpRequestException("synthetic checksum download failure");
                }

                return Task.FromResult(CreateResponse(checksumText, "text/plain"));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archiveBytes)
            };
            if (ResponseUri is not null)
            {
                response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, ResponseUri);
            }

            return Task.FromResult(response);
        }

        private static HttpResponseMessage CreateResponse(string content, string mediaType) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, mediaType)
            };
    }
}
