using Microsoft.Win32;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeLocatorTests
{
    [Fact]
    public void Constructor_creates_default_locator_without_external_activity()
    {
        // Arrange

        // Act
        var locator = new ChromeLocator();

        // Assert
        Assert.NotNull(locator);
    }

    [Fact]
    public void Find_returns_installation_from_existing_override_and_user_data_override()
    {
        // Arrange
        using var fixture = new TempFixture();
        var executablePath = fixture.File("chrome.exe");
        File.WriteAllText(executablePath, string.Empty);
        var userDataPath = fixture.Directory("User Data");
        Directory.CreateDirectory(userDataPath);
        var locator = new ChromeLocator(new TestEnvironment(fixture.Root));

        // Act
        var installation = locator.Find(executablePath, userDataPath);

        // Assert
        Assert.NotNull(installation);
        Assert.Equal(Path.GetFullPath(executablePath), installation.ExecutablePath);
        Assert.Equal(userDataPath, installation.UserDataDirectory);
    }

    [Fact]
    public void Find_returns_null_when_no_executable_can_be_found()
    {
        // Arrange
        using var fixture = new TempFixture();
        var environment = new TestEnvironment(fixture.Root);
        var locator = new ChromeLocator(environment);

        // Act
        var installation = locator.Find(fixture.File("missing.exe"), fixture.Directory("User Data"));

        // Assert
        Assert.Null(installation);
    }

    [Fact]
    public void Find_returns_null_when_executable_is_found_but_user_data_is_missing()
    {
        // Arrange
        using var fixture = new TempFixture();
        var executablePath = fixture.File("chrome.exe");
        File.WriteAllText(executablePath, string.Empty);
        var locator = new ChromeLocator(new TestEnvironment(fixture.Root));

        // Act
        var installation = locator.Find(executablePath);

        // Assert
        Assert.Null(installation);
    }

    [Fact]
    public void FindExecutable_returns_existing_override_as_full_path()
    {
        // Arrange
        using var fixture = new TempFixture();
        var executablePath = fixture.File("nested", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var locator = new ChromeLocator(new TestEnvironment(fixture.Root));

        // Act
        var result = locator.FindExecutable(Path.Combine(fixture.Root, "nested", ".", "chrome.exe"));

        // Assert
        Assert.Equal(Path.GetFullPath(executablePath), result);
    }

    [Fact]
    public void FindExecutable_returns_first_existing_standard_candidate()
    {
        // Arrange
        using var fixture = new TempFixture();
        var localAppData = fixture.Directory("Local App Data");
        var executablePath = Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var environment = new TestEnvironment(localAppData: localAppData);
        var locator = new ChromeLocator(environment);

        // Act
        var result = locator.FindExecutable();

        // Assert
        Assert.Equal(Path.GetFullPath(executablePath), result);
    }

    [Fact]
    public void FindExecutable_returns_null_when_override_and_standard_candidates_are_missing()
    {
        // Arrange
        using var fixture = new TempFixture();
        var locator = new ChromeLocator(new TestEnvironment(fixture.Root));

        // Act
        var result = locator.FindExecutable(fixture.File("missing.exe"));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindAll_finds_parent_user_data_for_an_installation()
    {
        // Arrange
        using var fixture = new TempFixture();
        var localAppData = fixture.Directory("Local App Data");
        var executablePath = Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        File.WriteAllText(CreateParentUserData(localAppData, executablePath), "state");
        var locator = new ChromeLocator(new TestEnvironment(localAppData: localAppData));

        // Act
        var installations = locator.FindAll();

        // Assert
        var installation = Assert.Single(installations);
        Assert.Equal(Path.GetFullPath(executablePath), installation.ExecutablePath);
        Assert.Equal(Path.Combine(localAppData, "Google", "Chrome", "User Data"), installation.UserDataDirectory);
    }

    [Fact]
    public void FindAll_finds_user_data_in_the_executable_directory()
    {
        // Arrange
        using var fixture = new TempFixture();
        var localAppData = fixture.Directory("Local App Data");
        var executablePath = Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var userDataPath = Path.Combine(localAppData, "Google", "Chrome", "Application", "User Data");
        Directory.CreateDirectory(userDataPath);
        File.WriteAllText(Path.Combine(userDataPath, "Local State"), "state");
        var locator = new ChromeLocator(new TestEnvironment(localAppData: localAppData));

        // Act
        var installations = locator.FindAll();

        // Assert
        var installation = Assert.Single(installations);
        Assert.Equal(userDataPath, installation.UserDataDirectory);
    }

    [Fact]
    public void FindAll_uses_standard_google_user_data_when_installation_has_no_local_fixture()
    {
        // Arrange
        using var fixture = new TempFixture();
        var localAppData = fixture.Directory("Local App Data");
        var programFiles = fixture.Directory("Program Files");
        var executablePath = Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var userDataPath = Path.Combine(localAppData, "Google", "Chrome", "User Data");
        Directory.CreateDirectory(userDataPath);
        File.WriteAllText(Path.Combine(userDataPath, "Local State"), "state");
        var locator = new ChromeLocator(new TestEnvironment(localAppData, programFiles));

        // Act
        var installations = locator.FindAll();

        // Assert
        var installation = Assert.Single(installations);
        Assert.Equal(Path.GetFullPath(executablePath), installation.ExecutablePath);
        Assert.Equal(userDataPath, installation.UserDataDirectory);
    }

    [Fact]
    public void FindAll_keeps_empty_user_data_when_no_strategy_matches()
    {
        // Arrange
        using var fixture = new TempFixture();
        var localAppData = fixture.Directory("Local App Data");
        var executablePath = Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var locator = new ChromeLocator(new TestEnvironment(localAppData: localAppData));

        // Act
        var installations = locator.FindAll();

        // Assert
        var installation = Assert.Single(installations);
        Assert.Equal(string.Empty, installation.UserDataDirectory);
    }

    [Fact]
    public void FindAll_returns_installation_when_executable_directory_has_no_parent()
    {
        // Arrange
        var executablePath = @"C:\chrome.exe";
        var environment = new TestEnvironment(
            existingFiles: new[] { executablePath },
            registryReader: hive => hive == RegistryHive.CurrentUser
                ? executablePath
                : null);
        var locator = new ChromeLocator(environment);

        // Act
        var installations = locator.FindAll();

        // Assert
        var installation = Assert.Single(installations);
        Assert.Equal(executablePath, installation.ExecutablePath);
        Assert.Equal(string.Empty, installation.UserDataDirectory);
    }

    [Fact]
    public void FindAll_keeps_empty_user_data_for_non_google_installation_without_user_data()
    {
        // Arrange
        using var fixture = new TempFixture();
        var programFiles = fixture.Directory("Program Files");
        var executablePath = Path.Combine(programFiles, "Chromium", "Application", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var environment = new TestEnvironment(
            programFiles: programFiles,
            registryReader: hive => hive == RegistryHive.CurrentUser ? executablePath : null);
        var locator = new ChromeLocator(environment);

        // Act
        var installations = locator.FindAll();

        // Assert
        var installation = Assert.Single(installations);
        Assert.Equal(string.Empty, installation.UserDataDirectory);
    }

    [Fact]
    public void FindAll_does_not_use_google_user_data_for_non_google_installation()
    {
        // Arrange
        using var fixture = new TempFixture();
        var localAppData = fixture.Directory("Local App Data");
        var programFiles = fixture.Directory("Program Files");
        var executablePath = Path.Combine(programFiles, "Chromium", "Application", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var userDataPath = Path.Combine(localAppData, "Google", "Chrome", "User Data");
        Directory.CreateDirectory(userDataPath);
        File.WriteAllText(Path.Combine(userDataPath, "Local State"), "state");
        var environment = new TestEnvironment(
            localAppData,
            programFiles,
            registryReader: hive => hive == RegistryHive.CurrentUser ? executablePath : null);
        var locator = new ChromeLocator(environment);

        // Act
        var installations = locator.FindAll();

        // Assert
        var installation = Assert.Single(installations);
        Assert.Equal(string.Empty, installation.UserDataDirectory);
    }

    [Fact]
    public void FindAll_ignores_user_data_directory_without_local_state()
    {
        // Arrange
        using var fixture = new TempFixture();
        var localAppData = fixture.Directory("Local App Data");
        var executablePath = Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        Directory.CreateDirectory(Path.Combine(localAppData, "Google", "Chrome", "User Data"));
        var locator = new ChromeLocator(new TestEnvironment(localAppData: localAppData));

        // Act
        var installations = locator.FindAll();

        // Assert
        var installation = Assert.Single(installations);
        Assert.Equal(string.Empty, installation.UserDataDirectory);
    }

    [Fact]
    public void FindUserDataDirectory_returns_existing_standard_directory()
    {
        // Arrange
        using var fixture = new TempFixture();
        var localAppData = fixture.Directory("Local App Data");
        var userDataPath = Path.Combine(localAppData, "Google", "Chrome", "User Data");
        Directory.CreateDirectory(userDataPath);
        var locator = new ChromeLocator(new TestEnvironment(localAppData: localAppData));

        // Act
        var result = locator.FindUserDataDirectory();

        // Assert
        Assert.Equal(userDataPath, result);
    }

    [Fact]
    public void FindUserDataDirectory_returns_null_when_standard_directory_is_missing()
    {
        // Arrange
        using var fixture = new TempFixture();
        var locator = new ChromeLocator(new TestEnvironment(fixture.Root));

        // Act
        var result = locator.FindUserDataDirectory();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindExecutable_swallows_local_machine_registry_read_failure()
    {
        // Arrange
        using var fixture = new TempFixture();
        var environment = new TestEnvironment(
            fixture.Root,
            registryReader: hive => hive == RegistryHive.LocalMachine
                ? throw new InvalidOperationException("registry unavailable")
                : null);
        var locator = new ChromeLocator(environment);

        // Act
        var result = locator.FindExecutable();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindExecutable_propagates_current_user_registry_read_failure()
    {
        // Arrange
        using var fixture = new TempFixture();
        var environment = new TestEnvironment(
            fixture.Root,
            registryReader: hive => hive == RegistryHive.CurrentUser
                ? throw new InvalidOperationException("registry unavailable")
                : null);
        var locator = new ChromeLocator(environment);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => locator.FindExecutable());

        // Assert
        Assert.Equal("registry unavailable", exception.Message);
    }

    private static string CreateParentUserData(string localAppData, string executablePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        var userDataPath = Path.Combine(localAppData, "Google", "Chrome", "User Data");
        Directory.CreateDirectory(userDataPath);
        return Path.Combine(userDataPath, "Local State");
    }

    private sealed class TestEnvironment : IChromeLocatorEnvironment
    {
        private readonly string _localAppData;
        private readonly string _programFiles;
        private readonly string _programFilesX86;
        private readonly Func<RegistryHive, string?> _registryReader;
        private readonly HashSet<string> _existingFiles;

        public TestEnvironment(
            string? localAppData = null,
            string? programFiles = null,
            string? programFilesX86 = null,
            Func<RegistryHive, string?>? registryReader = null,
            IEnumerable<string>? existingFiles = null)
        {
            _localAppData = localAppData ?? Path.Combine(Path.GetTempPath(), "missing-local-app-data");
            _programFiles = programFiles ?? Path.Combine(Path.GetTempPath(), "missing-program-files");
            _programFilesX86 = programFilesX86 ?? Path.Combine(Path.GetTempPath(), "missing-program-files-x86");
            _registryReader = registryReader ?? (_ => null);
            _existingFiles = new HashSet<string>(
                existingFiles ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        public string GetFolderPath(Environment.SpecialFolder folder) => folder switch
        {
            Environment.SpecialFolder.LocalApplicationData => _localAppData,
            Environment.SpecialFolder.ProgramFiles => _programFiles,
            Environment.SpecialFolder.ProgramFilesX86 => _programFilesX86,
            _ => string.Empty
        };

        public bool FileExists(string path) => _existingFiles.Contains(path) || (IsFixturePath(path) && System.IO.File.Exists(path));

        public bool DirectoryExists(string path) => IsFixturePath(path) && System.IO.Directory.Exists(path);

        private bool IsFixturePath(string path)
        {
            return path.StartsWith(_localAppData, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(_programFiles, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(_programFilesX86, StringComparison.OrdinalIgnoreCase);
        }

        public string? ReadRegistryExecutable(RegistryHive hive) => _registryReader(hive);
    }

    private sealed class TempFixture : IDisposable
    {
        public TempFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "RouterPlusChromeLocatorTests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Directory(params string[] segments)
        {
            return Path.Combine(new[] { Root }.Concat(segments).ToArray());
        }

        public string File(params string[] segments)
        {
            return Path.Combine(new[] { Root }.Concat(segments).ToArray());
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Root))
            {
                System.IO.Directory.Delete(Root, recursive: true);
            }
        }
    }
}
