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
            return FindUserDataDirectory(); // Fallback to default
        }

        // Check if it's CentBrowser
        if (executablePath.Contains("CentBrowser", StringComparison.OrdinalIgnoreCase))
        {
            var centBrowserUserData = Path.Combine(Path.GetDirectoryName(executableDir)!, "User Data");
            if (Directory.Exists(centBrowserUserData))
            {
                return centBrowserUserData;
            }
        }

        // Default: Google Chrome location
        return FindUserDataDirectory();
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
