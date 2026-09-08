using RouterPlus.Core.Updates;
using RouterPlus.Infrastructure.Updates;

namespace RouterPlus.Infrastructure.Tests;

public sealed class UpdateLaunchersTests
{
    [Fact]
    public async Task LaunchAsync_ReturnsFalse_WhenUpdaterPathDoesNotExist()
    {
        // Arrange
        var launcher = new WindowsUpdaterProcessLauncher();
        var updaterPath = Path.Combine(Path.GetTempPath(), $"router-plus-updater-{Guid.NewGuid():N}.exe");
        var version = ReleaseVersion.Parse("1.2.3");

        // Act
        var result = await launcher.LaunchAsync(
            updaterPath,
            Path.GetTempPath(),
            Path.GetTempPath(),
            Path.GetTempPath(),
            Environment.ProcessId,
            version);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task LaunchAsync_ReturnsFalse_WhenUpdaterPathIsDirectory()
    {
        // Arrange
        var launcher = new WindowsUpdaterProcessLauncher();
        var updaterDirectory = Path.Combine(Path.GetTempPath(), $"router-plus-updater-{Guid.NewGuid():N}");
        Directory.CreateDirectory(updaterDirectory);

        try
        {
            // Act
            var result = await launcher.LaunchAsync(
                updaterDirectory,
                Path.GetTempPath(),
                Path.GetTempPath(),
                Path.GetTempPath(),
                Environment.ProcessId,
                ReleaseVersion.Parse("1.2.3"));

            // Assert
            Assert.False(result);
        }
        finally
        {
            Directory.Delete(updaterDirectory);
        }
    }
}
