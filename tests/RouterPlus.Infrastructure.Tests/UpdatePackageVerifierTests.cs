using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RouterPlus.Core.Updates;
using RouterPlus.Infrastructure.Updates;

namespace RouterPlus.Infrastructure.Tests;

public sealed class UpdatePackageVerifierTests
{
    public static TheoryData<string> MalformedChecksumContents => new()
    {
        "",
        "not-a-checksum",
        $"{new string('0', 63)}  RouterPlus-v1.3.0-win-x64.zip",
        $"{new string('0', 65)}  RouterPlus-v1.3.0-win-x64.zip",
        $"{new string('g', 64)}  RouterPlus-v1.3.0-win-x64.zip",
        $"{new string('0', 64)}  other.zip",
        $"{new string('0', 64)}"
    };

    [Theory]
    [MemberData(nameof(MalformedChecksumContents))]
    public async Task VerifyAsync_rejects_malformed_checksum_format(string checksumContents)
    {
        using var package = await CreatePackageAsync();
        await File.WriteAllTextAsync(package.ChecksumPath, checksumContents);
        var verifier = new UpdatePackageVerifier();

        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0")));
    }

    [Fact]
    public async Task VerifyAsync_rejects_non_empty_staging_directory()
    {
        using var package = await CreatePackageAsync();
        Directory.CreateDirectory(package.StagingPath);
        await File.WriteAllTextAsync(Path.Combine(package.StagingPath, "existing.txt"), "existing");
        var verifier = new UpdatePackageVerifier();

        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0")));
    }

    [Theory]
    [InlineData("RouterPlus.exe")]
    [InlineData("RouterPlus.Updater.exe")]
    public async Task VerifyAsync_rejects_archive_missing_required_executable(string missingFile)
    {
        using var package = await CreatePackageAsync(
            missingFile == "RouterPlus.exe"
                ? ["RouterPlus.Updater.exe"]
                : ["RouterPlus.exe"]);
        var verifier = new UpdatePackageVerifier();

        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0")));
    }

    [Fact]
    public async Task VerifyAsync_rejects_duplicate_archive_entries()
    {
        using var package = await CreatePackageAsync(
            "RouterPlus.exe",
            "RouterPlus.Updater.exe",
            "RouterPlus.exe");
        var verifier = new UpdatePackageVerifier();

        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0")));
    }

    [Theory]
    [InlineData("../evil.exe")]
    [InlineData("..\\evil.exe")]
    [InlineData("/evil.exe")]
    [InlineData("./evil.exe")]
    [InlineData("dir//evil.exe")]
    public async Task VerifyAsync_rejects_traversal_or_invalid_archive_entry(string entryName)
    {
        using var package = await CreatePackageAsync(
            "RouterPlus.exe",
            "RouterPlus.Updater.exe",
            entryName);
        var verifier = new UpdatePackageVerifier();

        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0")));
    }

    [Fact]
    public async Task VerifyAsync_honors_pre_canceled_cancellation_token()
    {
        using var package = await CreatePackageAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var verifier = new UpdatePackageVerifier();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0"),
            cancellation.Token));
    }

    private static async Task<PackageFixture> CreatePackageAsync(params string[] entryNames)
    {
        if (entryNames.Length == 0)
        {
            entryNames = ["RouterPlus.exe", "RouterPlus.Updater.exe"];
        }

        var root = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "RouterPlus-v1.3.0-win-x64.zip");
        var checksumPath = archivePath + ".sha256";
        var stagingPath = Path.Combine(root, "staging");

        await using (var stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var entryName in entryNames)
            {
                await using var entry = archive.CreateEntry(entryName).Open();
                await entry.WriteAsync(Encoding.UTF8.GetBytes("test executable"));
            }
        }

        await using var archiveForHash = File.OpenRead(archivePath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(archiveForHash)).ToLowerInvariant();
        await File.WriteAllTextAsync(checksumPath, $"{hash}  {Path.GetFileName(archivePath)}");
        return new PackageFixture(root, archivePath, checksumPath, stagingPath);
    }

    private sealed class PackageFixture(
        string root,
        string archivePath,
        string checksumPath,
        string stagingPath) : IDisposable
    {
        public string Root { get; } = root;
        public string ArchivePath { get; } = archivePath;
        public string ChecksumPath { get; } = checksumPath;
        public string StagingPath { get; } = stagingPath;

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
