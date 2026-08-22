using System.IO.Compression;
using System.Security.Cryptography;
using RouterPlus.Core.Updates;

namespace RouterPlus.Infrastructure.Updates;

public sealed class UpdatePackageVerifier
{
    private const long MaxExtractedBytes = 512L * 1024 * 1024;
    private static readonly string[] RequiredFiles = ["RouterPlus.exe", "RouterPlus.Updater.exe"];

    public async Task<VerifiedUpdatePackage> VerifyAsync(
        string archivePath,
        string checksumPath,
        string stagingPath,
        ReleaseVersion expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksumPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingPath);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        RequireFile(archivePath);
        RequireFile(checksumPath);

        if (Directory.Exists(stagingPath) && Directory.EnumerateFileSystemEntries(stagingPath).Any())
        {
            throw new InvalidDataException("Update staging directory must be empty.");
        }

        var archiveHash = await ComputeSha256Async(archivePath, cancellationToken);
        var checksum = await ParseChecksumAsync(checksumPath, Path.GetFileName(archivePath), cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(archiveHash, checksum))
        {
            throw new InvalidDataException("Update archive checksum does not match the release checksum.");
        }

        Directory.CreateDirectory(stagingPath);
        var extractedFiles = await ExtractArchiveAsync(archivePath, stagingPath, cancellationToken);
        foreach (var requiredFile in RequiredFiles)
        {
            if (!extractedFiles.Contains(requiredFile))
            {
                throw new InvalidDataException($"Update package is missing required file '{requiredFile}'.");
            }

            UpdatePaths.ResolveUnderRoot(stagingPath, requiredFile);
        }

        return new VerifiedUpdatePackage(expectedVersion, archivePath, stagingPath);
    }

    private static async Task<HashSet<string>> ExtractArchiveAsync(
        string archivePath,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        var extractedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        long extractedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var normalizedName = NormalizeEntryName(entry.FullName);
            var isDirectory = normalizedName.EndsWith("/", StringComparison.Ordinal);
            var targetPath = UpdatePaths.ResolveUnderRoot(
                stagingPath,
                normalizedName.Replace('/', Path.DirectorySeparatorChar));
            if (!isDirectory && !extractedFiles.Add(normalizedName))
            {
                throw new InvalidDataException($"Update package contains duplicate entry '{normalizedName}'.");
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            extractedBytes = checked(extractedBytes + entry.Length);
            if (extractedBytes > MaxExtractedBytes)
            {
                throw new InvalidDataException("Update package exceeds the extraction size limit.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await using var entryStream = entry.Open();
            await using var outputStream = File.Create(targetPath);
            await entryStream.CopyToAsync(outputStream, cancellationToken);
        }

        return extractedFiles;
    }

    private static string NormalizeEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.Contains('\0'))
        {
            throw new InvalidDataException("Update package contains an invalid archive entry.");
        }

        var normalized = entryName.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/', StringSplitOptions.None).Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException($"Update package contains an unsafe archive entry '{entryName}'.");
        }

        return normalized;
    }

    private static async Task<byte[]> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static async Task<byte[]> ParseChecksumAsync(
        string checksumPath,
        string expectedFileName,
        CancellationToken cancellationToken)
    {
        var line = (await File.ReadAllTextAsync(checksumPath, cancellationToken)).Trim();
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !string.Equals(parts[^1], expectedFileName, StringComparison.Ordinal) ||
            parts[0].Length != 64 ||
            !parts[0].All(IsHexDigit))
        {
            throw new InvalidDataException("Update checksum file is malformed.");
        }

        return Convert.FromHexString(parts[0]);
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Update file was not found.", path);
        }
    }

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
