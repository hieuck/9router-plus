using System.Net.Http.Headers;
using RouterPlus.Core.Updates;

namespace RouterPlus.Infrastructure.Updates;

public sealed class SelfUpdateService : IUpdateService
{
    private const long MaxDownloadBytes = 700L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly GitHubReleaseClient _releaseClient;
    private readonly UpdatePackageVerifier _packageVerifier;
    private readonly IUpdaterProcessLauncher _updaterLauncher;
    private readonly ReleaseVersion _currentVersion;
    private readonly bool _isInstallSupported;

    public SelfUpdateService(
        HttpClient httpClient,
        ReleaseVersion currentVersion,
        IUpdaterProcessLauncher? updaterLauncher = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _packageVerifier = new UpdatePackageVerifier();
        _updaterLauncher = updaterLauncher ?? new WindowsUpdaterProcessLauncher();
        _isInstallSupported = OperatingSystem.IsWindows();
        _releaseClient = new GitHubReleaseClient(_httpClient, _currentVersion);
    }

    public bool IsInstallSupported => _isInstallSupported;

    public Task<ReleaseCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
        _releaseClient.GetLatestReleaseAsync(cancellationToken);

    public async Task<VerifiedUpdatePackage> DownloadAndStageAsync(
        ReleaseCheckResult release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!IsInstallSupported)
        {
            throw new InvalidOperationException("Self-update is only supported on Windows.");
        }

        if (!release.IsUpdateAvailable || release.AvailableVersion is null || release.Archive is null || release.Checksum is null)
        {
            throw new InvalidDataException("No verified update is available.");
        }

        var versionRoot = UpdatePaths.VersionRoot(release.AvailableVersion);
        if (Directory.Exists(versionRoot))
        {
            Directory.Delete(versionRoot, recursive: true);
        }

        Directory.CreateDirectory(versionRoot);
        var archivePath = UpdatePaths.ResolveUnderRoot(versionRoot, release.Archive.Name);
        var checksumPath = UpdatePaths.ResolveUnderRoot(versionRoot, release.Checksum.Name);
        var stagingPath = UpdatePaths.ResolveUnderRoot(versionRoot, "staging");

        try
        {
            await DownloadAssetAsync(release.Archive, archivePath, cancellationToken);
            await DownloadAssetAsync(release.Checksum, checksumPath, cancellationToken);

            return await _packageVerifier.VerifyAsync(
                archivePath,
                checksumPath,
                stagingPath,
                release.AvailableVersion,
                cancellationToken);
        }
        catch
        {
            if (Directory.Exists(versionRoot))
            {
                Directory.Delete(versionRoot, recursive: true);
            }

            throw;
        }
    }

    public Task<bool> LaunchUpdaterAsync(
        VerifiedUpdatePackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!IsInstallSupported)
        {
            return Task.FromResult(false);
        }

        var updaterPath = UpdatePaths.ResolveUnderRoot(package.StagingPath, "RouterPlus.Updater.exe");
        var versionRoot = Directory.GetParent(package.StagingPath)?.FullName
            ?? throw new InvalidDataException("Update staging path has no version root.");
        var backupPath = UpdatePaths.ResolveUnderRoot(versionRoot, "backup");
        var targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return _updaterLauncher.LaunchAsync(
            updaterPath,
            targetDirectory,
            package.StagingPath,
            backupPath,
            Environment.ProcessId,
            package.Version,
            cancellationToken);
    }

    private async Task DownloadAssetAsync(
        ReleaseAsset asset,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!asset.DownloadUri.IsAbsoluteUri
            || asset.DownloadUri.Scheme != Uri.UriSchemeHttps
            || !GitHubReleaseClient.IsAllowedAssetHost(asset.DownloadUri.Host)
            || asset.Length < 0
            || asset.Length > MaxDownloadBytes)
        {
            throw new InvalidDataException("Update asset URL or size is not allowed.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var finalUri = response.RequestMessage?.RequestUri ?? request.RequestUri;
        if (finalUri is null
            || !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !GitHubReleaseClient.IsAllowedAssetHost(finalUri.Host))
        {
            throw new InvalidDataException("Update asset redirect is not allowed.");
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
        {
            throw new InvalidDataException("Update asset is too large.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        var buffer = new byte[64 * 1024];
        long totalBytes = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes = checked(totalBytes + read);
            if (totalBytes > MaxDownloadBytes)
            {
                throw new InvalidDataException("Update asset is too large.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
