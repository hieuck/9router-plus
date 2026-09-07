using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeProfileReaderTests
{
    [Fact]
    public void Read_returns_profiles_from_local_state_in_parser_order()
    {
        // Arrange
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temporaryDirectory.Path, "Local State"),
            """
            {
              "profile": {
                "info_cache": {
                  "Default": { "name": "Personal" },
                  "Profile 2": { "name": "Automation" }
                }
              }
            }
            """);
        var reader = new ChromeProfileReader();

        // Act
        var profiles = reader.Read(temporaryDirectory.Path);

        // Assert
        Assert.Equal(2, profiles.Count);
        Assert.Equal("Automation", profiles[0].Name);
        Assert.Equal("Profile 2", profiles[0].DirectoryName);
        Assert.Equal("Personal", profiles[1].Name);
        Assert.True(profiles[1].IsDefault);
        Assert.All(profiles, profile => Assert.Equal(temporaryDirectory.Path, profile.UserDataDirectory));
    }

    [Fact]
    public void Read_returns_empty_when_local_state_is_missing()
    {
        // Arrange
        using var temporaryDirectory = new TemporaryDirectory();
        var reader = new ChromeProfileReader();

        // Act
        var profiles = reader.Read(temporaryDirectory.Path);

        // Assert
        Assert.Empty(profiles);
    }

    [Fact]
    public void Read_returns_empty_when_local_state_has_no_profile_cache()
    {
        // Arrange
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temporaryDirectory.Path, "Local State"), "{}");
        var reader = new ChromeProfileReader();

        // Act
        var profiles = reader.Read(temporaryDirectory.Path);

        // Assert
        Assert.Empty(profiles);
    }

    [Fact]
    public void Read_rejects_blank_user_data_directory()
    {
        // Arrange
        var reader = new ChromeProfileReader();

        // Act
        var exception = Assert.Throws<ArgumentException>(() => reader.Read(" "));

        // Assert
        Assert.Contains("userDataDirectory", exception.Message);
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
