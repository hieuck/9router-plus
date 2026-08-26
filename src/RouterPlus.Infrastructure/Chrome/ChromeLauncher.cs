using System.Diagnostics;
using System.Management;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Diagnostics;

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
        CancellationToken cancellationToken,
        bool useOriginalProfile = false)
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

        string userDataDirectory;
        string? tempUserDataDirectory = null;

        if (useOriginalProfile)
        {
            // Use original profile directly (no isolation)
            userDataDirectory = installation.UserDataDirectory;

            // Close any Chrome processes using this profile
            DebugConsole.WriteLine($"[ChromeLauncher] Closing Chrome processes using profile: {profile.DirectoryName}");
            CloseProcessesUsingProfile(installation.ExecutablePath, profile.DirectoryName);
        }
        else
        {
            // Use isolated temp profile (default)
            tempUserDataDirectory = Path.Combine(
                Path.GetTempPath(),
                $"routerplus_chrome_{Guid.NewGuid():N}");
            var tempProfileDirectory = Path.Combine(tempUserDataDirectory, profile.DirectoryName);

            Directory.CreateDirectory(tempProfileDirectory);
            CopyAuthenticationData(
                installation.UserDataDirectory,
                profile.ProfilePath,
                tempUserDataDirectory,
                tempProfileDirectory);

            userDataDirectory = tempUserDataDirectory;
        }

        try
        {
            var port = ChromeManagedSession.GetAvailableLoopbackPort();
            var sessionMarker = $"__9rp_session_{Guid.NewGuid():N}";
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
            startInfo.ArgumentList.Add($"--user-data-dir={userDataDirectory}");
            startInfo.ArgumentList.Add($"--profile-directory={profile.DirectoryName}");
            startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
            startInfo.ArgumentList.Add($"--remote-debugging-port={port}");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--no-default-browser-check");
            startInfo.ArgumentList.Add("--new-window");
            startInfo.ArgumentList.Add(markedUri.ToString());

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Chrome did not start.");

            try
            {
                var httpGet = _httpGetAsync ?? DefaultHttpGetAsync;
                var session = await ChromeManagedSession.CreateAsync(
                    process,
                    port,
                    sessionMarker,
                    TimeSpan.FromSeconds(30),
                    httpGet,
                    cancellationToken);

                // Only set temp directory for cleanup if using isolated profile
                if (tempUserDataDirectory != null)
                {
                    session.SetTempUserDataDirectory(tempUserDataDirectory);
                }

                return session;
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
                        // Best effort cleanup.
                    }
                }

                process.Dispose();
                throw;
            }
        }
        catch
        {
            if (tempUserDataDirectory != null)
            {
                TryDeleteDirectory(tempUserDataDirectory);
            }
            throw;
        }
    }

    private static void CopyAuthenticationData(
        string sourceUserDataDirectory,
        string sourceProfileDirectory,
        string destinationUserDataDirectory,
        string destinationProfileDirectory)
    {
        CopyFileIfPresent(
            Path.Combine(sourceUserDataDirectory, "Local State"),
            Path.Combine(destinationUserDataDirectory, "Local State"));

        foreach (var fileName in new[]
        {
            "Preferences",
            "Secure Preferences",
            "Cookies",
            "Cookies-journal",
            "Login Data",
            "Login Data-journal",
            "Web Data",
            "Web Data-journal"
        })
        {
            CopyFileIfPresent(
                Path.Combine(sourceProfileDirectory, fileName),
                Path.Combine(destinationProfileDirectory, fileName));
        }

        var sourceNetworkDirectory = Path.Combine(sourceProfileDirectory, "Network");
        var destinationNetworkDirectory = Path.Combine(destinationProfileDirectory, "Network");
        Directory.CreateDirectory(destinationNetworkDirectory);
        foreach (var fileName in new[] { "Cookies", "Cookies-journal" })
        {
            CopyFileIfPresent(
                Path.Combine(sourceNetworkDirectory, fileName),
                Path.Combine(destinationNetworkDirectory, fileName));
        }
    }

    private static void CopyFileIfPresent(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
        catch (IOException)
        {
            // A live browser may hold a profile database open. The isolated
            // session can still start and request credentials when necessary.
        }
        catch (UnauthorizedAccessException)
        {
            // A live browser may deny access to a profile database.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void CloseProcessesUsingProfile(string chromeExecutablePath, string profileDirectoryName)
    {
        try
        {
            var chromeProcessName = Path.GetFileNameWithoutExtension(chromeExecutablePath);
            var processes = Process.GetProcessesByName(chromeProcessName);

            var profileArg = $"--profile-directory={profileDirectoryName}";
            var killedCount = 0;

            foreach (var process in processes)
            {
                try
                {
                    // On Windows, check command line via WMI
                    if (OperatingSystem.IsWindows())
                    {
                        using var searcher = new System.Management.ManagementObjectSearcher(
                            $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");

                        foreach (System.Management.ManagementObject obj in searcher.Get())
                        {
                            var commandLine = obj["CommandLine"]?.ToString() ?? string.Empty;
                            if (commandLine.Contains(profileArg, StringComparison.OrdinalIgnoreCase))
                            {
                                DebugConsole.WriteLine($"[ChromeLauncher] Killing process {process.Id} using profile {profileDirectoryName}");
                                process.Kill();
                                killedCount++;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // On non-Windows, fall back to heuristic: kill all Chrome processes
                        // (safer to use isolated profiles on non-Windows)
                        process.Kill();
                        killedCount++;
                    }
                }
                catch
                {
                    // Process might have exited or access denied - continue
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (killedCount > 0)
            {
                DebugConsole.WriteLine($"[ChromeLauncher] Killed {killedCount} Chrome process(es) using profile {profileDirectoryName}");
                // Wait briefly for processes to fully exit and release locks
                System.Threading.Thread.Sleep(1000);
            }
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ChromeLauncher] Failed to close Chrome processes: {ex.Message}");
            // Non-fatal - proceed with launch attempt
        }
    }

    private static async Task<string> DefaultHttpGetAsync(string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        return await client.GetStringAsync(url, cancellationToken);
    }
}
