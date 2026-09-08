using Microsoft.Win32;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ChromeLocatorTests_{Guid.NewGuid():N}");

    public ChromeLocatorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Find_with_existing_executable_override_returns_full_path_and_override_user_data()
    {
        var executable = CreateFile("override", "chrome.exe");
        var locator = CreateLocator();

        var installation = locator.Find(executable, "custom-user-data");

        Assert.NotNull(installation);
        Assert.Equal(Path.GetFullPath(executable), installation.ExecutablePath);
        Assert.Equal("custom-user-data", installation.UserDataDirectory);
    }

    [Fact]
    public void Find_returns_null_when_no_executable_is_found()
    {
        var locator = CreateLocator();

        var installation = locator.Find(Path.Combine(_root, "missing.exe"), "user-data");

        Assert.Null(installation);
    }

    [Fact]
    public void Find_uses_discovered_user_data_when_override_is_blank()
    {
        var executable = CreateFile("Google", "Chrome", "Application", "chrome.exe");
        var userData = Path.Combine(_root, "Google", "Chrome", "User Data");
        Directory.CreateDirectory(userData);

        var locator = CreateLocator(
            folders: new Dictionary<Environment.SpecialFolder, string>
            {
                [Environment.SpecialFolder.LocalApplicationData] = _root,
            });

        var installation = locator.Find(executable, " ");

        Assert.NotNull(installation);
        Assert.Equal(Path.GetFullPath(executable), installation.ExecutablePath);
        Assert.Equal(userData, installation.UserDataDirectory);
    }

    [Fact]
    public void FindExecutable_prefers_override_then_local_app_data_then_registry()
    {
        var overrideExecutable = CreateFile("override.exe");
        var localExecutable = CreateFile("local", "Google", "Chrome", "Application", "chrome.exe");
        var registryExecutable = CreateFile("registry", "chrome.exe");
        var locator = CreateLocator(
            folders: new Dictionary<Environment.SpecialFolder, string>
            {
                [Environment.SpecialFolder.LocalApplicationData] = Path.Combine(_root, "local"),
                [Environment.SpecialFolder.ProgramFiles] = Path.Combine(_root, "program-files"),
            },
            registryExecutable: registryExecutable);

        Assert.Equal(Path.GetFullPath(overrideExecutable), locator.FindExecutable(overrideExecutable));
        Assert.Equal(Path.GetFullPath(localExecutable), locator.FindExecutable());

        File.Delete(localExecutable);
        Assert.Equal(Path.GetFullPath(registryExecutable), locator.FindExecutable());
    }

    [Fact]
    public void FindAll_discovers_standard_and_additional_paths_with_strategy_specific_user_data()
    {
        var centBrowser = CreateFile("Program Files", "CentBrowser", "chrome.exe");
        var centBrowserUserData = Path.Combine(_root, "Program Files", "User Data");
        Directory.CreateDirectory(centBrowserUserData);
        CreateFile("Program Files", "User Data", "Local State");

        var flatCentBrowser = CreateFile("CentBrowser", "chrome.exe");
        Directory.CreateDirectory(Path.Combine(_root, "CentBrowser", "User Data"));
        CreateFile("CentBrowser", "User Data", "Local State");

        var googleChrome = CreateFile("LocalAppData", "Google", "Chrome", "Application", "chrome.exe");
        Directory.CreateDirectory(Path.Combine(_root, "LocalAppData", "Google", "Chrome", "User Data"));
        CreateFile("LocalAppData", "Google", "Chrome", "User Data", "Local State");

        var locator = CreateLocator(
            folders: new Dictionary<Environment.SpecialFolder, string>
            {
                [Environment.SpecialFolder.LocalApplicationData] = Path.Combine(_root, "LocalAppData"),
            },
            drives: new[] { _root });

        var installations = locator.FindAll();

        Assert.Equal(3, installations.Count);
        Assert.Contains(installations, installation =>
            installation.ExecutablePath.Equals(Path.GetFullPath(centBrowser), StringComparison.OrdinalIgnoreCase) &&
            installation.UserDataDirectory == centBrowserUserData);
        Assert.Contains(installations, installation =>
            installation.ExecutablePath.Equals(Path.GetFullPath(flatCentBrowser), StringComparison.OrdinalIgnoreCase) &&
            installation.UserDataDirectory == Path.Combine(_root, "CentBrowser", "User Data"));
        Assert.Contains(installations, installation =>
            installation.ExecutablePath.Equals(Path.GetFullPath(googleChrome), StringComparison.OrdinalIgnoreCase) &&
            installation.UserDataDirectory == Path.Combine(_root, "LocalAppData", "Google", "Chrome", "User Data"));
    }

    [Fact]
    public void FindAll_deduplicates_registry_and_standard_candidates()
    {
        var executable = CreateFile("LocalAppData", "Google", "Chrome", "Application", "chrome.exe");
        var locator = CreateLocator(
            folders: new Dictionary<Environment.SpecialFolder, string>
            {
                [Environment.SpecialFolder.LocalApplicationData] = Path.Combine(_root, "LocalAppData"),
            },
            registryExecutable: executable);

        var installations = locator.FindAll();

        var matching = installations.Where(installation =>
            installation.ExecutablePath.Equals(Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase));
        Assert.Single(matching);
    }

    [Fact]
    public void FindUserDataDirectory_returns_existing_standard_directory_only()
    {
        var locator = CreateLocator(
            folders: new Dictionary<Environment.SpecialFolder, string>
            {
                [Environment.SpecialFolder.LocalApplicationData] = Path.Combine(_root, "LocalAppData"),
            });

        Assert.Null(locator.FindUserDataDirectory());

        var expected = Path.Combine(_root, "LocalAppData", "Google", "Chrome", "User Data");
        Directory.CreateDirectory(expected);
        Assert.Equal(expected, locator.FindUserDataDirectory());
    }

    private ChromeLocator CreateLocator(
        IReadOnlyDictionary<Environment.SpecialFolder, string>? folders = null,
        string? registryExecutable = null,
        IReadOnlyList<string>? drives = null)
    {
        folders ??= new Dictionary<Environment.SpecialFolder, string>();
        return new ChromeLocator(
            folderPath: folder => folders.TryGetValue(folder, out var path) ? path : string.Empty,
            fileExists: File.Exists,
            directoryExists: Directory.Exists,
            registryExecutable: _ => registryExecutable,
            drives: drives ?? Array.Empty<string>());
    }

    private string CreateFile(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
