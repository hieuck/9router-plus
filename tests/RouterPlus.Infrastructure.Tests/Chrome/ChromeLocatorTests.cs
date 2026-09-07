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
