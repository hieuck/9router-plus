using Microsoft.Win32;

namespace RouterPlus.Infrastructure.Chrome;

public sealed class ChromeLocator
{
    private static readonly string[] DefaultDrives = { "C:", "D:", "E:", "F:", "G:", "H:" };

    private readonly Func<Environment.SpecialFolder, string> _folderPath;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<RegistryHive, string?> _registryExecutable;
    private readonly IReadOnlyList<string> _drives;

    public ChromeLocator()
        : this(
            Environment.GetFolderPath,
            File.Exists,
            Directory.Exists,
            ReadRegistryExecutable,
            DefaultDrives)
    {
    }

    internal ChromeLocator(
        Func<Environment.SpecialFolder, string> folderPath,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        Func<RegistryHive, string?> registryExecutable,
        IReadOnlyList<string> drives)
    {
        _folderPath = folderPath;
        _fileExists = fileExists;
        _directoryExists = directoryExists;
        _registryExecutable = registryExecutable;
        _drives = drives;
    }

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
        if (!string.IsNullOrWhiteSpace(executableOverride) && _fileExists(executableOverride))
        {
            return Path.GetFullPath(executableOverride);
        }

        var localAppData = _folderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = _folderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = _folderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new[]
        {
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            _registryExecutable(RegistryHive.CurrentUser),
            _registryExecutable(RegistryHive.LocalMachine)
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && _fileExists(path));
    }

    public IReadOnlyList<ChromeInstallation> FindAll()
    {
        var localAppData = _folderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = _folderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = _folderPath(Environment.SpecialFolder.ProgramFilesX86);

        var executableCandidates = new[]
        {
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            _registryExecutable(RegistryHive.CurrentUser),
            _registryExecutable(RegistryHive.LocalMachine)
        };

        // Search for Chromium-based browsers in common locations across all drives
        var drives = _drives;
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
            .Where(path => !string.IsNullOrWhiteSpace(path) && _fileExists(path))
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
            if (_directoryExists(candidate) && _fileExists(Path.Combine(candidate, "Local State")))
            {
                return candidate;
            }
        }

        // Strategy 2: Check executable's own directory (for flat installs)
        // e.g. G:\Program Files\CentBrowser\chrome.exe -> look for G:\Program Files\CentBrowser\User Data
        var sameDirCandidate = Path.Combine(executableDir, "User Data");
        if (_directoryExists(sameDirCandidate) && _fileExists(Path.Combine(sameDirCandidate, "Local State")))
        {
            return sameDirCandidate;
        }

        // Strategy 3: For Google Chrome, try standard LocalAppData location
        if (executablePath.Contains("Google", StringComparison.OrdinalIgnoreCase))
        {
            var localAppData = _folderPath(Environment.SpecialFolder.LocalApplicationData);
            var googleUserData = Path.Combine(localAppData, "Google", "Chrome", "User Data");
            if (_directoryExists(googleUserData) && _fileExists(Path.Combine(googleUserData, "Local State")))
            {
                return googleUserData;
            }
        }

        return null;
    }

    public string? FindUserDataDirectory()
    {
        var localAppData = _folderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidate = Path.Combine(localAppData, "Google", "Chrome", "User Data");
        return _directoryExists(candidate) ? candidate : null;
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
