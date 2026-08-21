using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RouterPlus.Core.Updates;
using RouterPlus.Infrastructure.Updates;
using Xunit.Sdk;

namespace RouterPlus.Core.Tests;

public sealed class UpdatePackageValidationTests
{
    [Fact]
    public async Task VerifyAsync_accepts_matching_checksum_manifest_signature_and_required_files()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: false);
        var verifier = new UpdatePackageVerifier(new TestSignatureVerifier());

        var result = await verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.ManifestPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0"),
            "Test Publisher");

        Assert.Equal("1.3.0", result.Manifest.Version.ToString());
        Assert.True(File.Exists(Path.Combine(package.StagingPath, "RouterPlus.exe")));
        Assert.True(File.Exists(Path.Combine(package.StagingPath, "RouterPlus.Updater.exe")));
    }

    [Fact]
    public async Task VerifyAsync_rejects_mismatched_checksum()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: false);
        await File.WriteAllTextAsync(package.ChecksumPath, $"{new string('0', 64)}  {Path.GetFileName(package.ArchivePath)}");
        var verifier = new UpdatePackageVerifier(new TestSignatureVerifier());

        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.ManifestPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0"),
            "Test Publisher"));
    }

    [Fact]
    public async Task VerifyAsync_rejects_missing_manifest_signature()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: false, signature: "");
        var verifier = new UpdatePackageVerifier(new TestSignatureVerifier());

        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.ManifestPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0"),
            "Test Publisher"));
    }

    [Fact]
    public async Task VerifyAsync_rejects_invalid_manifest_signature()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: false);
        var verifier = new UpdatePackageVerifier(new TestSignatureVerifier { ManifestValid = false });

        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.ManifestPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0"),
            "Test Publisher"));
    }

    [Fact]
    public async Task VerifyAsync_rejects_zip_path_traversal()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: true);
        var verifier = new UpdatePackageVerifier(new TestSignatureVerifier());

        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
            package.ArchivePath,
            package.ChecksumPath,
            package.ManifestPath,
            package.StagingPath,
            ReleaseVersion.Parse("1.3.0"),
            "Test Publisher"));
    }

    [Fact]
    public void ResolveUnderRoot_rejects_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidDataException>(() => UpdatePaths.ResolveUnderRoot(root, "..\\outside.txt"));
    }

    [Fact]
    public void ResolveUnderRoot_rejects_existing_reparse_point()
    {
        var root = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var link = Path.Combine(root, "linked");

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(outside);
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw SkipException.ForSkip("The test environment cannot create a reparse point.");
        }
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw SkipException.ForSkip("The test environment cannot create a reparse point.");
        }

        Assert.Throws<InvalidDataException>(() => UpdatePaths.ResolveUnderRoot(root, "linked\\file.txt"));
    }

    private static async Task<PackageFixture> CreatePackageAsync(
        bool includeTraversalEntry,
        string signature = "signed-manifest")
    {
        var root = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "RouterPlus-v1.3.0-win-x64.zip");
        var checksumPath = archivePath + ".sha256";
        var manifestPath = Path.Combine(root, "RouterPlus-v1.3.0-manifest.json");
        var stagingPath = Path.Combine(root, "staging");

        await using (var stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var fileName in new[] { "RouterPlus.exe", "RouterPlus.Updater.exe" })
            {
                await using var file = archive.CreateEntry(fileName).Open();
                await file.WriteAsync(Encoding.UTF8.GetBytes("test executable"));
            }

            if (includeTraversalEntry)
            {
                await using var file = archive.CreateEntry("..\\evil.exe").Open();
                await file.WriteAsync(Encoding.UTF8.GetBytes("evil"));
            }
        }

        var hash = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(archivePath))).ToLowerInvariant();
        await File.WriteAllTextAsync(checksumPath, $"{hash}  {Path.GetFileName(archivePath)}");
        var manifest = new
        {
            version = "1.3.0",
            channel = "stable",
            assetName = Path.GetFileName(archivePath),
            sha256 = hash,
            publisher = "Test Publisher",
            signature
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));
        return new PackageFixture(archivePath, checksumPath, manifestPath, stagingPath);
    }

    private sealed record PackageFixture(
        string ArchivePath,
        string ChecksumPath,
        string ManifestPath,
        string StagingPath);

    private sealed class TestSignatureVerifier : IUpdateSignatureVerifier
    {
        public bool ManifestValid { get; init; } = true;

        public bool VerifyManifest(string manifestPath, ReleaseManifest manifest, string expectedPublisher) => ManifestValid;

        public bool VerifyExecutable(string executablePath, string expectedPublisher) => true;
    }
}
