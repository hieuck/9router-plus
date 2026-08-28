using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RouterPlus.Core.Chrome;

namespace RouterPlus.Infrastructure.Chrome;

public sealed class ChromeProfileDeleter
{
    private readonly Action<string, string> _closeBrowserProcesses;

    public ChromeProfileDeleter(Action<string, string>? closeBrowserProcesses = null)
    {
        _closeBrowserProcesses = closeBrowserProcesses ?? CloseBrowserProcesses;
    }

    public void Delete(ChromeProfile profile, string userDataDirectory) =>
        Delete(profile, userDataDirectory, chromeExecutablePath: null);

    public void Delete(ChromeProfile profile, string userDataDirectory, string? chromeExecutablePath)
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

        if (!string.IsNullOrWhiteSpace(chromeExecutablePath))
        {
            _closeBrowserProcesses(chromeExecutablePath, expectedUserDataDirectory);
        }

        RemoveProfileFromLocalState(userDataDirectory, profile.DirectoryName);

        if (Directory.Exists(profilePath))
        {
            Directory.Delete(profilePath, recursive: true);
        }
    }

    private static void CloseBrowserProcesses(string chromeExecutablePath, string userDataDirectory)
    {
        var expectedExecutablePath = NormalizePath(chromeExecutablePath);
        var processes = Process.GetProcessesByName("chrome")
            .Where(process => IsMatchingBrowserProcess(process, expectedExecutablePath))
            .ToArray();

        foreach (var process in processes)
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    process.CloseMainWindow();
                }
            }
            catch
            {
                // Process may have exited while closing its window.
            }
        }

        if (processes.Length > 0)
        {
            Thread.Sleep(1500);
        }

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Process may have exited or denied termination.
            }
            finally
            {
                process.Dispose();
            }
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
        var root = JsonNode.Parse(json);
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
