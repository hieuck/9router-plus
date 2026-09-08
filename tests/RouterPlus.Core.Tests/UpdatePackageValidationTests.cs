using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RouterPlus.Core.Updates;
using RouterPlus.Infrastructure.Updates;
using Xunit.Sdk;

namespace RouterPlus.Core.Tests;

public sealed class UpdatePackageValidationTests
{
    [Fact]
    public async Task VerifyAsync_accepts_unsigned_archive_with_matching_checksum_and_required_files()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: false);
        var verifier = new UpdatePackageVerifier();

        try
        {
            var result = await verifier.VerifyAsync(
                package.ArchivePath,
                package.ChecksumPath,
                package.StagingPath,
                ReleaseVersion.Parse("1.3.0"));

            Assert.Equal("1.3.0", result.Version.ToString());
            Assert.True(File.Exists(Path.Combine(package.StagingPath, "RouterPlus.exe")));
            Assert.True(File.Exists(Path.Combine(package.StagingPath, "RouterPlus.Updater.exe")));
        }
        finally
        {
            DeletePackageRoot(package);
        }
    }

    [Fact]
    public async Task VerifyAsync_rejects_mismatched_checksum()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: false);
        await File.WriteAllTextAsync(package.ChecksumPath, $"{new string('0', 64)}  {Path.GetFileName(package.ArchivePath)}");
        var verifier = new UpdatePackageVerifier();

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
                package.ArchivePath,
                package.ChecksumPath,
                package.StagingPath,
                ReleaseVersion.Parse("1.3.0")));
        }
        finally
        {
            DeletePackageRoot(package);
        }
    }

    [Fact]
    public async Task VerifyAsync_rejects_zip_path_traversal()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: true);
        var verifier = new UpdatePackageVerifier();

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
                package.ArchivePath,
                package.ChecksumPath,
                package.StagingPath,
                ReleaseVersion.Parse("1.3.0")));
        }
        finally
        {
            DeletePackageRoot(package);
        }
    }

    [Fact]
    public async Task VerifyAsync_rejects_a_non_empty_staging_directory()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: false);
        Directory.CreateDirectory(package.StagingPath);
        await File.WriteAllTextAsync(Path.Combine(package.StagingPath, "existing.txt"), "existing");
        var verifier = new UpdatePackageVerifier();

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
                package.ArchivePath,
                package.ChecksumPath,
                package.StagingPath,
                ReleaseVersion.Parse("1.3.0")));
        }
        finally
        {
            DeletePackageRoot(package);
        }
    }

    [Fact]
    public async Task VerifyAsync_rejects_a_package_missing_a_required_file()
    {
        var package = await CreatePackageAsync(["RouterPlus.exe"]);
        var verifier = new UpdatePackageVerifier();

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
                package.ArchivePath,
                package.ChecksumPath,
                package.StagingPath,
                ReleaseVersion.Parse("1.3.0")));
        }
        finally
        {
            DeletePackageRoot(package);
        }
    }

    [Fact]
    public async Task VerifyAsync_rejects_duplicate_archive_entries()
    {
        var package = await CreatePackageAsync(["RouterPlus.exe", "RouterPlus.Updater.exe", "RouterPlus.exe"]);
        var verifier = new UpdatePackageVerifier();

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
                package.ArchivePath,
                package.ChecksumPath,
                package.StagingPath,
                ReleaseVersion.Parse("1.3.0")));
        }
        finally
        {
            DeletePackageRoot(package);
        }
    }

    [Fact]
    public async Task VerifyAsync_rejects_a_malformed_checksum_file()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: false);
        await File.WriteAllTextAsync(package.ChecksumPath, "not-a-checksum");
        var verifier = new UpdatePackageVerifier();

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
                package.ArchivePath,
                package.ChecksumPath,
                package.StagingPath,
                ReleaseVersion.Parse("1.3.0")));
        }
        finally
        {
            DeletePackageRoot(package);
        }
    }

    [Fact]
    public async Task VerifyAsync_rejects_missing_archive_file()
    {
        var package = await CreatePackageAsync(includeTraversalEntry: false);
        var verifier = new UpdatePackageVerifier();

        try
        {
            File.Delete(package.ArchivePath);

            await Assert.ThrowsAsync<FileNotFoundException>(() => verifier.VerifyAsync(
                package.ArchivePath,
                package.ChecksumPath,
                package.StagingPath,
                ReleaseVersion.Parse("1.3.0")));
        }
        finally
        {
            DeletePackageRoot(package);
        }
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
        var link = Path.Combine(root, "linked");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        try
        {
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
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(outside))
            {
                Directory.Delete(outside, recursive: true);
            }
        }
    }

    private static Task<PackageFixture> CreatePackageAsync(bool includeTraversalEntry) =>
        CreatePackageAsync(includeTraversalEntry
            ? ["RouterPlus.exe", "RouterPlus.Updater.exe", "..\\evil.exe"]
            : ["RouterPlus.exe", "RouterPlus.Updater.exe"]);

    private static async Task<PackageFixture> CreatePackageAsync(IReadOnlyList<string> entries)
    {
        var root = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "RouterPlus-v1.3.0-win-x64.zip");
        var checksumPath = archivePath + ".sha256";
        var stagingPath = Path.Combine(root, "staging");

        await using (var stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var entryName in entries)
            {
                var entry = archive.CreateEntry(entryName);
                if (!entryName.EndsWith("/", StringComparison.Ordinal))
                {
                    await using var file = entry.Open();
                    await file.WriteAsync(Encoding.UTF8.GetBytes("test executable"));
                }
            }
        }

        await using var archiveFile = File.OpenRead(archivePath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(archiveFile)).ToLowerInvariant();
        await File.WriteAllTextAsync(checksumPath, $"{hash}  {Path.GetFileName(archivePath)}");
        return new PackageFixture(root, archivePath, checksumPath, stagingPath);
    }

    private static void DeletePackageRoot(PackageFixture package)
    {
        if (Directory.Exists(package.Root))
        {
            Directory.Delete(package.Root, recursive: true);
        }
    }

    private sealed record PackageFixture(
        string Root,
        string ArchivePath,
        string ChecksumPath,
        string StagingPath);
}
