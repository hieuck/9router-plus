using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeLauncherTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RouterPlusTests",
        Guid.NewGuid().ToString("N"));

    public ChromeLauncherTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Launch_NullInstallation_ThrowsArgumentNullException()
    {
        // Arrange
        var launcher = new ChromeLauncher();
        var profile = CreateProfile();

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() =>
            launcher.Launch(null!, profile, "https://example.test"));

        // Assert
        Assert.Equal("installation", exception.ParamName);
    }

    [Fact]
    public void Launch_NullProfile_ThrowsArgumentNullException()
    {
        // Arrange
        var launcher = new ChromeLauncher();
        var installation = CreateInstallation();

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() =>
            launcher.Launch(installation, null!, "https://example.test"));

        // Assert
        Assert.Equal("profile", exception.ParamName);
    }

    [Fact]
    public void Launch_NullStartUrl_ThrowsArgumentNullException()
    {
        // Arrange
        var launcher = new ChromeLauncher();
        var installation = CreateInstallation();
        var profile = CreateProfile();

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() =>
            launcher.Launch(installation, profile, null!));

        // Assert
        Assert.Equal("startUrl", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Launch_WhitespaceStartUrl_ThrowsArgumentException(string startUrl)
    {
        // Arrange
        var launcher = new ChromeLauncher();
        var installation = CreateInstallation();
        var profile = CreateProfile();

        // Act
        var exception = Assert.Throws<ArgumentException>(() =>
            launcher.Launch(installation, profile, startUrl));

        // Assert
        Assert.Equal("startUrl", exception.ParamName);
    }

    [Fact]
    public void Launch_MissingExecutable_ThrowsFileNotFoundException()
    {
        // Arrange
        var launcher = new ChromeLauncher();
        var installation = new ChromeInstallation(
            Path.Combine(_tempDirectory, "missing-chrome.exe"),
            _tempDirectory);
        var profile = CreateProfile();

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            launcher.Launch(installation, profile, "https://example.test"));

        // Assert
        Assert.Equal(installation.ExecutablePath, exception.FileName);
    }

    [Fact]
    public void Launch_MissingProfileDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var launcher = new ChromeLauncher();
        var installation = CreateInstallation();
        var profile = new ChromeProfile(
            "profile-id",
            "Test Profile",
            "Missing Profile",
            _tempDirectory,
            false);

        // Act
        var exception = Assert.Throws<DirectoryNotFoundException>(() =>
            launcher.Launch(installation, profile, "https://example.test"));

        // Assert
        Assert.Contains(profile.DirectoryName, exception.Message);
    }

    private ChromeInstallation CreateInstallation()
    {
        var executablePath = Path.Combine(_tempDirectory, "chrome.exe");
        File.WriteAllText(executablePath, "test executable placeholder");
        return new ChromeInstallation(executablePath, _tempDirectory);
    }

    private ChromeProfile CreateProfile()
    {
        var profileDirectory = Path.Combine(_tempDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        return new ChromeProfile(
            "profile-id",
            "Test Profile",
            "Profile 1",
            _tempDirectory,
            false);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
