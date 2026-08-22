using Microsoft.Win32;

namespace RouterPlus.Infrastructure.Chrome;

public sealed class ChromeLocator
{
    public ChromeInstallation? Find(string? executableOverride = null, string? userDataOverride = null)
    {
        var executable = FindExecutable(executableOverride);
        if (executable is null)
        {
            return null;
        }

        var userDataDirectory = string.IsNullOrWhiteSpace(userDataOverride)
            ? FindUserDataDirectory()
            : userDataOverride;
        if (string.IsNullOrWhiteSpace(userDataDirectory))
        {
            return null;
        }

        return new ChromeInstallation(executable, userDataDirectory);
    }

    public string? FindExecutable(string? executableOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(executableOverride) && File.Exists(executableOverride))
        {
            return Path.GetFullPath(executableOverride);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new[]
        {
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            ReadRegistryExecutable(RegistryHive.CurrentUser),
            ReadRegistryExecutable(RegistryHive.LocalMachine)
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    public IReadOnlyList<ChromeInstallation> FindAll()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var executableCandidates = new[]
        {
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            ReadRegistryExecutable(RegistryHive.CurrentUser),
            ReadRegistryExecutable(RegistryHive.LocalMachine)
        };

        // Search for Chromium-based browsers in common locations across all drives
        var drives = new[] { "C:", "D:", "E:", "F:", "G:", "H:" };
        var browserSubPaths = new[]
        {
            Path.Combine("Program Files", "CentBrowser", "chrome.exe"),
            Path.Combine("Program Files", "CentBrowser", "Application", "chrome.exe"),
            Path.Combine("CentBrowser", "chrome.exe"),
            Path.Combine("CentBrowser", "Application", "chrome.exe"),
            Path.Combine("Program Files", "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine("Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine("Program Files", "Chromium", "Application", "chrome.exe"),
            Path.Combine("Program Files", "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
        };

        var additionalSearchPaths = new List<string>();
        foreach (var drive in drives)
        {
            foreach (var subPath in browserSubPaths)
            {
                additionalSearchPaths.Add(Path.Combine(drive + "\\", subPath));
            }
        }

        var allCandidates = executableCandidates.Concat(additionalSearchPaths);
        var foundExecutables = allCandidates
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var installations = new List<ChromeInstallation>();
        foreach (var executable in foundExecutables)
        {
            var userDataDirectory = FindUserDataDirectoryForExecutable(executable);
            installations.Add(new ChromeInstallation(executable, userDataDirectory ?? string.Empty));
        }

        return installations;
    }

    private string? FindUserDataDirectoryForExecutable(string executablePath)
    {
        var executableDir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDir))
        {
            return null;
        }

        // Strategy 1: Check parent directory of executable (for CentBrowser, portable Chrome)
        // e.g. G:\Program Files\CentBrowser\chrome.exe -> look for G:\Program Files\CentBrowser\User Data
        var parentDir = Path.GetDirectoryName(executableDir);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            var candidate = Path.Combine(parentDir, "User Data");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Local State")))
            {
                return candidate;
            }
        }

        // Strategy 2: Check executable's own directory (for flat installs)
        // e.g. G:\Program Files\CentBrowser\chrome.exe -> look for G:\Program Files\CentBrowser\User Data
        var sameDirCandidate = Path.Combine(executableDir, "User Data");
        if (Directory.Exists(sameDirCandidate) && File.Exists(Path.Combine(sameDirCandidate, "Local State")))
        {
            return sameDirCandidate;
        }

        // Strategy 3: For Google Chrome, try standard LocalAppData location
        if (executablePath.Contains("Google", StringComparison.OrdinalIgnoreCase))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var googleUserData = Path.Combine(localAppData, "Google", "Chrome", "User Data");
            if (Directory.Exists(googleUserData) && File.Exists(Path.Combine(googleUserData, "Local State")))
            {
                return googleUserData;
            }
        }

        return null;
    }

    public string? FindUserDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidate = Path.Combine(localAppData, "Google", "Chrome", "User Data");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string? ReadRegistryExecutable(RegistryHive hive)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            return key?.GetValue(null) as string;
        }
        catch (Exception) when (hive == RegistryHive.LocalMachine)
        {
            return null;
        }
    }
}
