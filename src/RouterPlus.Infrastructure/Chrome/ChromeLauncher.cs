using System.Diagnostics;
using RouterPlus.Core.Chrome;

namespace RouterPlus.Infrastructure.Chrome;

public sealed class ChromeLauncher
{
    private readonly Func<string, CancellationToken, Task<string>>? _httpGetAsync;

    public ChromeLauncher()
    {
    }

    internal ChromeLauncher(Func<string, CancellationToken, Task<string>> httpGetAsync)
    {
        _httpGetAsync = httpGetAsync;
    }

    public Process Launch(
        ChromeInstallation installation,
        ChromeProfile profile,
        string startUrl)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(startUrl);

        if (!File.Exists(installation.ExecutablePath))
        {
            throw new FileNotFoundException("Chrome executable was not found.", installation.ExecutablePath);
        }

        if (!Directory.Exists(profile.ProfilePath))
        {
            throw new DirectoryNotFoundException($"Chrome profile directory was not found: {profile.DirectoryName}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = installation.ExecutablePath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installation.ExecutablePath) ?? Environment.CurrentDirectory
        };
        startInfo.ArgumentList.Add($"--user-data-dir={installation.UserDataDirectory}");
        startInfo.ArgumentList.Add($"--profile-directory={profile.DirectoryName}");
        startInfo.ArgumentList.Add(startUrl);

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Chrome did not start.");
    }

    public async Task<ChromeManagedSession> LaunchManagedAsync(
        ChromeInstallation installation,
        ChromeProfile profile,
        Uri startUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(startUri);

        if (!File.Exists(installation.ExecutablePath))
        {
            throw new FileNotFoundException("Chrome executable was not found.", installation.ExecutablePath);
        }

        if (!Directory.Exists(profile.ProfilePath))
        {
            throw new DirectoryNotFoundException($"Chrome profile directory was not found: {profile.DirectoryName}");
        }

        var port = ChromeManagedSession.GetAvailableLoopbackPort();

        // Generate unique session marker for target identification
        var sessionMarker = $"__9rp_session_{Guid.NewGuid():N}";

        // Append session marker as fragment (removed after first navigation)
        var markedUri = new UriBuilder(startUri)
        {
            Fragment = sessionMarker
        }.Uri;

        var startInfo = new ProcessStartInfo
        {
            FileName = installation.ExecutablePath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installation.ExecutablePath) ?? Environment.CurrentDirectory
        };
        startInfo.ArgumentList.Add($"--user-data-dir={installation.UserDataDirectory}");
        startInfo.ArgumentList.Add($"--profile-directory={profile.DirectoryName}");
        startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        startInfo.ArgumentList.Add($"--remote-debugging-port={port}");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("--new-window");
        startInfo.ArgumentList.Add(markedUri.ToString());

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Chrome did not start.");

        try
        {
            var httpGet = _httpGetAsync ?? DefaultHttpGetAsync;
            return await ChromeManagedSession.CreateAsync(
                process,
                port,
                sessionMarker,
                TimeSpan.FromSeconds(15),
                httpGet,
                cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill();
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
            process.Dispose();
            throw;
        }
    }

    private static async Task<string> DefaultHttpGetAsync(string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        return await client.GetStringAsync(url, cancellationToken);
    }
}
