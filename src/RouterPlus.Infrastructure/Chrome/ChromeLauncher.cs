using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
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
            var killed = CloseProcessesUsingProfile(installation.ExecutablePath, profile.DirectoryName);

            if (!killed)
            {
                DebugConsole.WriteLine($"[ChromeLauncher] WARNING: No processes killed. Profile may be open in another Chrome variant or browser.");
            }
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
            var markedUri = AppendSessionMarker(startUri, sessionMarker);

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
            startInfo.ArgumentList.Add("--disable-session-crashed-bubble");
            startInfo.ArgumentList.Add("--hide-crash-restore-bubble");
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

    private static Uri AppendSessionMarker(Uri originalUri, string sessionMarker)
    {
        ArgumentNullException.ThrowIfNull(originalUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionMarker);

        var uriBuilder = new UriBuilder(originalUri);

        // Preserve original fragment (contains device user_code for AWS flows)
        // Append session marker as a query parameter instead
        var query = uriBuilder.Query?.TrimStart('?') ?? string.Empty;
        var sessionParam = $"__9rp_session={Uri.EscapeDataString(sessionMarker)}";
        uriBuilder.Query = string.IsNullOrEmpty(query)
            ? sessionParam
            : $"{query}&{sessionParam}";

        return uriBuilder.Uri;
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

    private static bool CloseProcessesUsingProfile(string chromeExecutablePath, string profileDirectoryName)
    {
        try
        {
            // Close visible browser windows gracefully so Chromium can persist session tabs
            // before any remaining helper processes are force-terminated.
            var processes = Process.GetProcessesByName("chrome");
            CloseVisibleBrowserWindows(processes);

            // Kill ALL chrome.exe processes (CentBrowser, Brave, Chrome share process name).
            // This is required for auto-login because Chrome variants use single-instance with profile locking.
            // Trying to launch a managed Chrome while another Chrome variant holds the user-data-dir lock will fail.
            processes = Process.GetProcessesByName("chrome");

            var killedCount = 0;
            var skippedCount = 0;

            foreach (var process in processes)
            {
                try
                {
                    // Skip helper processes that don't hold profile locks (crashpad, network service with no --type)
                    // Kill all main, utility, renderer, gpu processes that reference user-data-dir
                    if (OperatingSystem.IsWindows())
                    {
                        using var searcher = new System.Management.ManagementObjectSearcher(
                            $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");

                        foreach (System.Management.ManagementObject obj in searcher.Get())
                        {
                            var commandLine = obj["CommandLine"]?.ToString() ?? string.Empty;

                            // Skip crashpad-handler (doesn't hold profile locks)
                            if (commandLine.Contains("--type=crashpad-handler"))
                            {
                                skippedCount++;
                                break;
                            }

                            // Skip pure network/storage services without profile lock (they may be from another instance)
                            // But for safety, kill them anyway since they share user-data-dir
                            DebugConsole.WriteLine($"[ChromeLauncher] Killing process {process.Id} to release user-data-dir locks");
                            process.Kill();
                            killedCount++;
                            break;
                        }
                    }
                    else
                    {
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

            DebugConsole.WriteLine($"[ChromeLauncher] Killed {killedCount} Chrome process(es), skipped {skippedCount} crashpad handler(s)");

            if (killedCount > 0)
            {
                // Wait briefly for processes to fully exit and release locks
                System.Threading.Thread.Sleep(1500);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ChromeLauncher] Failed to close Chrome processes: {ex.Message}");
            // Non-fatal - proceed with launch attempt
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;

    private static void CloseVisibleBrowserWindows(Process[] processes)
    {
        try
        {
            var closedCount = 0;
            foreach (var process in processes)
            {
                try
                {
                    // Find main window handle
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        // Send WM_CLOSE to allow graceful shutdown
                        PostMessage(process.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        closedCount++;
                    }
                }
                catch
                {
                    // Process might have no window or exited
                }
            }

            if (closedCount > 0)
            {
                DebugConsole.WriteLine($"[ChromeLauncher] Sent WM_CLOSE to {closedCount} browser window(s)");
                // Wait for graceful shutdown before force-killing remaining processes
                System.Threading.Thread.Sleep(2000);
            }
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ChromeLauncher] Failed to close browser windows gracefully: {ex.Message}");
        }
    }

    private static async Task<string> DefaultHttpGetAsync(string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        return await client.GetStringAsync(url, cancellationToken);
    }
}
