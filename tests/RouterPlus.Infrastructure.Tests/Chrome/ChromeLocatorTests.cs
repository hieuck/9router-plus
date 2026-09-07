using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeLocatorTests
{
    [Fact]
    public void FindExecutable_returns_full_path_for_existing_override()
    {
        // Arrange
        using var temporaryDirectory = new TemporaryDirectory();
        var executablePath = Path.Combine(temporaryDirectory.Path, "nested", "chrome.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, "synthetic executable");
        var locator = new ChromeLocator();

        // Act
        var result = locator.FindExecutable(executablePath);

        // Assert
        Assert.Equal(Path.GetFullPath(executablePath), result);
    }

    [Fact]
    public void Find_with_existing_executable_and_user_data_overrides_returns_installation()
    {
        // Arrange
        using var temporaryDirectory = new TemporaryDirectory();
        var executablePath = Path.Combine(temporaryDirectory.Path, "chrome.exe");
        var userDataDirectory = Path.Combine(temporaryDirectory.Path, "User Data");
        File.WriteAllText(executablePath, "synthetic executable");
        Directory.CreateDirectory(userDataDirectory);
        var locator = new ChromeLocator();

        // Act
        var installation = locator.Find(executablePath, userDataDirectory);

        // Assert
        Assert.NotNull(installation);
        Assert.Equal(Path.GetFullPath(executablePath), installation.ExecutablePath);
        Assert.Equal(userDataDirectory, installation.UserDataDirectory);
    }

    [Fact]
    public void Find_with_nonblank_user_data_override_does_not_require_browser_files()
    {
        // Arrange
        using var temporaryDirectory = new TemporaryDirectory();
        var executablePath = Path.Combine(temporaryDirectory.Path, "chrome.exe");
        var userDataDirectory = Path.Combine(temporaryDirectory.Path, "not-created");
        File.WriteAllText(executablePath, "synthetic executable");
        var locator = new ChromeLocator();

        // Act
        var installation = locator.Find(executablePath, userDataDirectory);

        // Assert
        Assert.NotNull(installation);
        Assert.Equal(userDataDirectory, installation.UserDataDirectory);
    }

    [Fact]
    public void FindUserDataDirectory_matches_standard_local_app_data_candidate()
    {
        // Arrange
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expected = Path.Combine(localAppData, "Google", "Chrome", "User Data");
        var locator = new ChromeLocator();

        // Act
        var result = locator.FindUserDataDirectory();

        // Assert
        Assert.Equal(Directory.Exists(expected) ? expected : null, result);
    }

    [Fact]
    public void FindAll_returns_distinct_existing_executables_without_launching_browser()
    {
        // Arrange
        var locator = new ChromeLocator();

        // Act
        var installations = locator.FindAll();

        // Assert
        Assert.Equal(
            installations.Select(installation => installation.ExecutablePath),
            installations.Select(installation => installation.ExecutablePath)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.All(installations, installation => Assert.True(File.Exists(installation.ExecutablePath)));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"routerplus-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
