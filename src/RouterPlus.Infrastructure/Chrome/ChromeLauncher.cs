using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

public sealed class ChromeLauncher
{
    private readonly Func<string, CancellationToken, Task<string>>? _httpGetAsync;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly Action<string> _createDirectory;
    private readonly Action<string, string, bool> _copyFile;
    private readonly Action<string, bool> _deleteDirectory;
    private readonly Func<ProcessStartInfo, Process?> _processStart;
    private readonly Func<
        Process,
        int,
        string,
        TimeSpan,
        Func<string, CancellationToken, Task<string>>,
        CancellationToken,
        Task<ChromeManagedSession>> _sessionFactory;
    private readonly Func<int> _getAvailableLoopbackPort;

    public ChromeLauncher()
        : this(
            null,
            File.Exists,
            Directory.Exists,
            path => Directory.CreateDirectory(path),
            File.Copy,
            Directory.Delete,
            Process.Start,
            ChromeManagedSession.CreateAsync,
            ChromeManagedSession.GetAvailableLoopbackPort)
    {
    }

    internal ChromeLauncher(Func<string, CancellationToken, Task<string>> httpGetAsync)
        : this(
            httpGetAsync,
            File.Exists,
            Directory.Exists,
            path => Directory.CreateDirectory(path),
            File.Copy,
            Directory.Delete,
            Process.Start,
            ChromeManagedSession.CreateAsync,
            ChromeManagedSession.GetAvailableLoopbackPort)
    {
    }

    internal ChromeLauncher(
        Func<string, CancellationToken, Task<string>>? httpGetAsync,
        Func<ProcessStartInfo, Process?> startProcess)
        : this(
            httpGetAsync,
            File.Exists,
            Directory.Exists,
            path => Directory.CreateDirectory(path),
            File.Copy,
            Directory.Delete,
            startProcess,
            ChromeManagedSession.CreateAsync,
            ChromeManagedSession.GetAvailableLoopbackPort)
    {
    }

    internal ChromeLauncher(
        Func<string, CancellationToken, Task<string>>? httpGetAsync,
        Func<ProcessStartInfo, Process?> startProcess,
        Func<int> getAvailableLoopbackPort)
        : this(
            httpGetAsync,
            File.Exists,
            Directory.Exists,
            path => Directory.CreateDirectory(path),
            File.Copy,
            Directory.Delete,
            startProcess,
            ChromeManagedSession.CreateAsync,
            getAvailableLoopbackPort)
    {
    }

    internal ChromeLauncher(
        Func<string, CancellationToken, Task<string>>? httpGetAsync,
        Func<ProcessStartInfo, Process?> startProcess,
        Func<Process, int, string, TimeSpan, Func<string, CancellationToken, Task<string>>, CancellationToken, Task<ChromeManagedSession>> createManagedSession)
        : this(
            httpGetAsync,
            File.Exists,
            Directory.Exists,
            path => Directory.CreateDirectory(path),
            File.Copy,
            Directory.Delete,
            startProcess,
            createManagedSession,
            ChromeManagedSession.GetAvailableLoopbackPort)
    {
    }

    internal ChromeLauncher(
        Func<string, CancellationToken, Task<string>>? httpGetAsync,
        Func<ProcessStartInfo, Process?>? processStart,
        Func<Process, int, string, TimeSpan, Func<string, CancellationToken, Task<string>>, CancellationToken, Task<ChromeManagedSession>>? createManagedSession,
        Func<int>? getAvailableLoopbackPort)
        : this(
            httpGetAsync,
            File.Exists,
            Directory.Exists,
            path => Directory.CreateDirectory(path),
            File.Copy,
            Directory.Delete,
            processStart ?? Process.Start,
            createManagedSession ?? ChromeManagedSession.CreateAsync,
            getAvailableLoopbackPort ?? ChromeManagedSession.GetAvailableLoopbackPort)
    {
    }

    internal ChromeLauncher(
        Func<string, CancellationToken, Task<string>>? httpGetAsync,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        Action<string> createDirectory,
        Action<string, string, bool> copyFile,
        Action<string, bool> deleteDirectory,
        Func<ProcessStartInfo, Process?> processStart,
        Func<
            Process,
            int,
            string,
            TimeSpan,
            Func<string, CancellationToken, Task<string>>,
            CancellationToken,
            Task<ChromeManagedSession>> sessionFactory)
        : this(
            httpGetAsync,
            fileExists,
            directoryExists,
            createDirectory,
            copyFile,
            deleteDirectory,
            processStart,
            sessionFactory,
            ChromeManagedSession.GetAvailableLoopbackPort)
    {
    }

    private ChromeLauncher(
        Func<string, CancellationToken, Task<string>>? httpGetAsync,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        Action<string> createDirectory,
        Action<string, string, bool> copyFile,
        Action<string, bool> deleteDirectory,
        Func<ProcessStartInfo, Process?> processStart,
        Func<
            Process,
            int,
            string,
            TimeSpan,
            Func<string, CancellationToken, Task<string>>,
            CancellationToken,
            Task<ChromeManagedSession>> sessionFactory,
        Func<int> getAvailableLoopbackPort)
    {
        _httpGetAsync = httpGetAsync;
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _directoryExists = directoryExists ?? throw new ArgumentNullException(nameof(directoryExists));
        _createDirectory = createDirectory ?? throw new ArgumentNullException(nameof(createDirectory));
        _copyFile = copyFile ?? throw new ArgumentNullException(nameof(copyFile));
        _deleteDirectory = deleteDirectory ?? throw new ArgumentNullException(nameof(deleteDirectory));
        _processStart = processStart ?? throw new ArgumentNullException(nameof(processStart));
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _getAvailableLoopbackPort = getAvailableLoopbackPort ?? throw new ArgumentNullException(nameof(getAvailableLoopbackPort));
    }


    public Process Launch(
        ChromeInstallation installation,
        ChromeProfile profile,
        string startUrl)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(startUrl);

        if (!_fileExists(installation.ExecutablePath))
        {
            throw new FileNotFoundException("Chrome executable was not found.", installation.ExecutablePath);
        }

        if (!_directoryExists(profile.ProfilePath))
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

        return _processStart(startInfo) ?? throw new InvalidOperationException("Chrome did not start.");
    }

    public async Task<ChromeManagedSession> LaunchManagedAsync(
        ChromeInstallation installation,
        ChromeProfile profile,
        Uri startUri,
        CancellationToken cancellationToken,
        bool useOriginalProfile = false,
        bool minimized = false)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(startUri);

        if (!_fileExists(installation.ExecutablePath))
        {
            throw new FileNotFoundException("Chrome executable was not found.", installation.ExecutablePath);
        }

        if (!_directoryExists(profile.ProfilePath))
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
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "ChromeLauncher",
                "ClosingChromeProcesses",
                "Closing Chrome processes using profile",
                new { profile_name = profile.DirectoryName });
            var killed = CloseProcessesUsingProfile(
                installation.UserDataDirectory,
                profile.DirectoryName);

            if (!killed)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Warning,
                    "ChromeLauncher",
                    "NoProcessesKilled",
                    "No processes killed. Profile may be open in another Chrome variant or browser",
                    new { profile_name = profile.DirectoryName });
            }
        }
        else
        {
            // Use isolated temp profile (default)
            tempUserDataDirectory = Path.Combine(
                Path.GetTempPath(),
                $"routerplus_chrome_{Guid.NewGuid():N}");
            var tempProfileDirectory = Path.Combine(tempUserDataDirectory, profile.DirectoryName);

            _createDirectory(tempProfileDirectory);
            CopyAuthenticationData(
                installation.UserDataDirectory,
                profile.ProfilePath,
                tempUserDataDirectory,
                tempProfileDirectory);

            userDataDirectory = tempUserDataDirectory;
        }

        try
        {
            var port = _getAvailableLoopbackPort();
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

            var process = _processStart(startInfo)
                ?? throw new InvalidOperationException("Chrome did not start.");

            try
            {
                var httpGet = _httpGetAsync ?? DefaultHttpGetAsync;
                var session = await _sessionFactory(
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
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        await process.WaitForExitAsync(cancellationToken);
                    }
                }
                catch
                {
                    // Best effort cleanup.
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

    internal static Uri AppendSessionMarker(Uri originalUri, string sessionMarker)
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

    private void CopyAuthenticationData(
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
        _createDirectory(destinationNetworkDirectory);
        foreach (var fileName in new[] { "Cookies", "Cookies-journal" })
        {
            CopyFileIfPresent(
                Path.Combine(sourceNetworkDirectory, fileName),
                Path.Combine(destinationNetworkDirectory, fileName));
        }
    }

    private void CopyFileIfPresent(string sourcePath, string destinationPath)
    {
        if (!_fileExists(sourcePath))
        {
            return;
        }

        try
        {
            _copyFile(sourcePath, destinationPath, true);
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

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (_directoryExists(path))
            {
                _deleteDirectory(path, true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static bool CloseProcessesUsingProfile(
        string userDataDirectory,
        string profileDirectoryName)
    {
        try
        {
            var normalizedUserDataDirectory = Path.GetFullPath(userDataDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var processes = Process.GetProcessesByName("chrome");
            CloseVisibleBrowserWindows(processes);
            processes = Process.GetProcessesByName("chrome");

            var killedCount = 0;
            var skippedCount = 0;

            foreach (var process in processes)
            {
                try
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        continue;
                    }

                    using var searcher = new System.Management.ManagementObjectSearcher(
                        $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        var commandLine = obj["CommandLine"]?.ToString() ?? string.Empty;
                        if (commandLine.Contains("--type=crashpad-handler", StringComparison.OrdinalIgnoreCase))
                        {
                            skippedCount++;
                            break;
                        }

                        if (!CommandLineUsesUserDataDirectory(commandLine, normalizedUserDataDirectory))
                        {
                            skippedCount++;
                            break;
                        }

                        ObservabilityHub.Instance.LogEvent(
                            LogLevel.Info,
                            "ChromeLauncher",
                            "KillingProcess",
                            "Killing process to release user-data-dir locks",
                            new { process_id = process.Id, profile_name = profileDirectoryName });
                        process.Kill();
                        killedCount++;
                        break;
                    }
                }
                catch
                {
                    // Process might have exited or access denied - continue.
                }
                finally
                {
                    process.Dispose();
                }
            }

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "ChromeLauncher",
                "ProcessesKilled",
                "Killed Chrome processes",
                new { killed_count = killedCount, skipped_count = skippedCount });

            if (killedCount > 0)
            {
                System.Threading.Thread.Sleep(1500);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "ChromeLauncher",
                "CloseProcessesFailed",
                "Failed to close browser processes",
                new { error = ex.Message });
            return false;
        }
    }

    private static bool CommandLineUsesUserDataDirectory(string commandLine, string normalizedUserDataDirectory)
    {
        const string prefix = "--user-data-dir=";
        var start = commandLine.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        while (start >= 0)
        {
            start += prefix.Length;
            var end = commandLine.IndexOf(' ', start);
            var value = end < 0 ? commandLine[start..] : commandLine[start..end];
            value = value.Trim('"');
            try
            {
                var normalized = Path.GetFullPath(value)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(normalized, normalizedUserDataDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Ignore malformed command-line values.
            }

            start = commandLine.IndexOf(prefix, start, StringComparison.OrdinalIgnoreCase);
        }

        return false;
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
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "ChromeLauncher",
                    "WindowsClosed",
                    "Sent WM_CLOSE to browser windows",
                    new { closed_count = closedCount });
                // Wait for graceful shutdown before force-killing remaining processes
                System.Threading.Thread.Sleep(2000);
            }
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "ChromeLauncher",
                "CloseWindowsFailed",
                "Failed to close browser windows gracefully",
                new { error = ex.Message });
        }
    }

    private static async Task<string> DefaultHttpGetAsync(string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        return await client.GetStringAsync(url, cancellationToken);
    }
}
