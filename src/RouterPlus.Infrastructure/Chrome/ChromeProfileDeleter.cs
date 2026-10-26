using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RouterPlus.Core.Chrome;

namespace RouterPlus.Infrastructure.Chrome;

public sealed class ChromeProfileDeleter
{
    private readonly Func<string, string, CancellationToken, Task> _closeBrowserProcesses;
    private readonly Func<string, string, Process[]> _getProcesses;
    private readonly Func<Process, bool> _hasExited;
    private readonly Action<Process> _closeMainWindow;
    private readonly Action<Process> _killProcess;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<CancellationToken, Task> _delay;

    public ChromeProfileDeleter(Func<string, string, CancellationToken, Task>? closeBrowserProcesses = null)
        : this(closeBrowserProcesses, null, null, null, null, null, null)
    {
    }

    internal ChromeProfileDeleter(
        Func<string, string, CancellationToken, Task>? closeBrowserProcesses,
        Func<string, string, Process[]>? getProcesses,
        Func<Process, bool>? hasExited,
        Action<Process>? closeMainWindow,
        Action<Process>? killProcess,
        Func<DateTimeOffset>? clock,
        Func<CancellationToken, Task>? delay)
    {
        _getProcesses = getProcesses ?? ((executable, _) => DefaultGetProcesses(executable));
        _hasExited = hasExited ?? DefaultHasExited;
        _closeMainWindow = closeMainWindow ?? DefaultCloseMainWindow;
        _killProcess = killProcess ?? DefaultKillProcess;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? (cancellationToken => Task.Delay(100, cancellationToken));

        if (closeBrowserProcesses is not null)
        {
            _closeBrowserProcesses = closeBrowserProcesses;
        }
        else
        {
            _closeBrowserProcesses = (executable, userDataDirectory, cancellationToken) =>
                CloseBrowserProcesses(executable, userDataDirectory, cancellationToken,
                    _getProcesses, _hasExited, _closeMainWindow, _killProcess, _clock, _delay);
        }
    }

    public Task DeleteAsync(ChromeProfile profile, string userDataDirectory, CancellationToken cancellationToken = default) =>
        DeleteAsync(profile, userDataDirectory, chromeExecutablePath: null, cancellationToken);

    public async Task DeleteAsync(ChromeProfile profile, string userDataDirectory, string? chromeExecutablePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);

        Validate(profile, userDataDirectory);

        cancellationToken.ThrowIfCancellationRequested();

        var expectedUserDataDirectory = NormalizePath(userDataDirectory);
        if (!string.IsNullOrWhiteSpace(chromeExecutablePath))
        {
            await _closeBrowserProcesses(chromeExecutablePath, expectedUserDataDirectory, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var profilePath = NormalizePath(profile.ProfilePath);
        RemoveProfileFromLocalState(userDataDirectory, profile.DirectoryName);

        if (Directory.Exists(profilePath))
        {
            Directory.Delete(profilePath, recursive: true);
        }
    }

    public void Validate(ChromeProfile profile, string userDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);

        var expectedUserDataDirectory = NormalizePath(userDataDirectory);
        var profileUserDataDirectory = NormalizePath(profile.UserDataDirectory);
        if (!string.Equals(profileUserDataDirectory, expectedUserDataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Chrome profile is outside the configured User Data directory.");
        }

        var profilePath = NormalizePath(profile.ProfilePath);
        var profileParent = Directory.GetParent(profilePath)?.FullName;
        if (string.Equals(profilePath, expectedUserDataDirectory, StringComparison.OrdinalIgnoreCase)
            || profileParent is null
            || !string.Equals(NormalizePath(profileParent), expectedUserDataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Chrome profile directory must be an immediate child of User Data.");
        }

        if (File.Exists(profilePath))
        {
            throw new InvalidOperationException("The Chrome profile path is not a directory.");
        }
    }

    internal static async Task CloseBrowserProcesses(
        string chromeExecutablePath,
        string userDataDirectory,
        CancellationToken cancellationToken = default,
        Func<string, string, Process[]>? getProcesses = null,
        Func<Process, bool>? hasExited = null,
        Action<Process>? closeMainWindow = null,
        Action<Process>? killProcess = null,
        Func<DateTimeOffset>? clock = null,
        Func<CancellationToken, Task>? delay = null)
    {
        var resolvedGetProcesses = getProcesses ?? ((executable, _) => DefaultGetProcesses(executable));
        var resolvedHasExited = hasExited ?? DefaultHasExited;
        var resolvedCloseMainWindow = closeMainWindow ?? DefaultCloseMainWindow;
        var resolvedKillProcess = killProcess ?? DefaultKillProcess;
        var resolvedClock = clock ?? (() => DateTimeOffset.UtcNow);
        var resolvedDelay = delay ?? (token => Task.Delay(100, token));

        var processes = resolvedGetProcesses(chromeExecutablePath, userDataDirectory) ?? Array.Empty<Process>();

        foreach (var process in processes)
        {
            try
            {
                resolvedCloseMainWindow(process);
            }
            catch
            {
                // Process may have exited while closing its window.
            }
        }

        if (processes.Length > 0)
        {
            var deadline = resolvedClock() + TimeSpan.FromMilliseconds(1500);
            while (processes.Any(process => !SafeHasExited(resolvedHasExited, process)) && resolvedClock() < deadline)
            {
                await resolvedDelay(cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var process in processes)
        {
            try
            {
                if (!SafeHasExited(resolvedHasExited, process))
                {
                    try
                    {
                        resolvedKillProcess(process);
                    }
                    catch
                    {
                        // Process may have exited or denied termination.
                    }
                }
            }
            catch
            {
                // Process may have exited or denied termination.
            }
            finally
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }
    }

    private static Process[] DefaultGetProcesses(string chromeExecutablePath)
    {
        var expectedExecutablePath = NormalizePath(chromeExecutablePath);
        return Process.GetProcessesByName("chrome")
            .Where(process => IsMatchingBrowserProcess(process, expectedExecutablePath))
            .ToArray();
    }

    private static bool DefaultHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static void DefaultCloseMainWindow(Process process)
    {
        if (process.MainWindowHandle != IntPtr.Zero)
        {
            process.CloseMainWindow();
        }
    }

    private static void DefaultKillProcess(Process process)
    {
        process.Kill(entireProcessTree: true);
    }

    private static bool SafeHasExited(Func<Process, bool> hasExited, Process process)
    {
        try
        {
            return hasExited(process);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsMatchingBrowserProcess(Process process, string expectedExecutablePath)
    {
        try
        {
            var executablePath = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(executablePath)
                && string.Equals(NormalizePath(executablePath), expectedExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveProfileFromLocalState(string userDataDirectory, string directoryName)
    {
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        if (!File.Exists(localStatePath))
        {
            return;
        }

        var json = File.ReadAllText(localStatePath);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        if (root is null)
        {
            return;
        }

        var profile = root["profile"]?.AsObject();
        if (profile is null)
        {
            return;
        }

        var changed = profile["info_cache"]?.AsObject()?.Remove(directoryName) == true;
        changed |= RemoveProfileFromArray(profile, "profiles_order", directoryName);
        changed |= RemoveProfileFromArray(profile, "last_active_profiles", directoryName);
        if (changed)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(localStatePath, root.ToJsonString(options));
        }
    }

    private static bool RemoveProfileFromArray(JsonObject profile, string propertyName, string directoryName)
    {
        if (profile[propertyName] is not JsonArray profiles)
        {
            return false;
        }

        var changed = false;
        for (var index = profiles.Count - 1; index >= 0; index--)
        {
            if (profiles[index] is JsonValue value
                && value.TryGetValue<string>(out var valueString)
                && string.Equals(valueString, directoryName, StringComparison.OrdinalIgnoreCase))
            {
                profiles.RemoveAt(index);
                changed = true;
            }
        }

        return changed;
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
