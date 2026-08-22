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

        // Search for other Chromium-based browsers in common locations
        var additionalSearchPaths = new[]
        {
            Path.Combine(programFiles, "CentBrowser", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "CentBrowser", "Application", "chrome.exe"),
            Path.Combine(localAppData, "CentBrowser", "Application", "chrome.exe"),
            "G:\\CentBrowser\\Application\\chrome.exe",
            "D:\\CentBrowser\\Application\\chrome.exe",
            "C:\\CentBrowser\\Application\\chrome.exe"
        };

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
            if (!string.IsNullOrWhiteSpace(userDataDirectory))
            {
                installations.Add(new ChromeInstallation(executable, userDataDirectory));
            }
        }

        return installations;
    }

    private string? FindUserDataDirectoryForExecutable(string executablePath)
    {
        // Try to infer User Data location based on executable path
        var executableDir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDir))
        {
            return null;
        }

        // Get the Application folder's parent (should be browser root)
        var browserRoot = Path.GetDirectoryName(executableDir);
        if (string.IsNullOrWhiteSpace(browserRoot))
        {
            return null;
        }

        // Try User Data in the same root as executable
        var userDataCandidate = Path.Combine(browserRoot, "User Data");
        if (Directory.Exists(userDataCandidate))
        {
            // Verify it's a valid Chrome User Data directory
            var localStateFile = Path.Combine(userDataCandidate, "Local State");
            if (File.Exists(localStateFile))
            {
                return userDataCandidate;
            }
        }

        // For Google Chrome in LocalAppData, try the standard location
        if (executablePath.Contains("Google\\Chrome", StringComparison.OrdinalIgnoreCase))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var googleChromeUserData = Path.Combine(localAppData, "Google", "Chrome", "User Data");
            if (Directory.Exists(googleChromeUserData))
            {
                var localStateFile = Path.Combine(googleChromeUserData, "Local State");
                if (File.Exists(localStateFile))
                {
                    return googleChromeUserData;
                }
            }
        }

        // Could not find valid User Data directory
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
