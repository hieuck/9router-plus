using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.App.Tests.ViewModels;

public sealed class MainViewModelHealthTests
{
    [Fact]
    public async Task CheckAllProfilesHealthAsync_UpdatesAllProfileHealthStatus()
    {
        // Arrange: MainViewModel with 3 profiles, real ProfileHealthService
        var healthService = new ProfileHealthService();
        var viewModel = new MainViewModel(profileHealthService: healthService);

        var profile1 = CreateProfile("Profile1", "Profile 1");
        var profile2 = CreateProfile("Profile2", "Profile 2");
        var profile3 = CreateProfile("Profile3", "Profile 3");

        viewModel.Profiles.Add(profile1);
        viewModel.Profiles.Add(profile2);
        viewModel.Profiles.Add(profile3);

        // Verify initially null
        Assert.All(viewModel.ProfileRows, row => Assert.Null(row.HealthStatus));

        // Act: Execute CheckAllProfilesHealthAsync
        await viewModel.CheckAllProfilesHealthAsync();

        // Assert: All 3 ProfileRowViewModel.HealthStatus are non-null
        Assert.Equal(3, viewModel.ProfileRows.Count);
        Assert.All(viewModel.ProfileRows, row => Assert.NotNull(row.HealthStatus));
        Assert.All(viewModel.ProfileRows, row => Assert.NotNull(row.HealthStatus!.Message));
        Assert.All(viewModel.ProfileRows, row => Assert.NotEqual(HealthLevel.Unknown, row.HealthStatus!.Level));
    }

    [Fact]
    public async Task CheckProfileHealthAsync_UpdatesSingleProfileHealthStatus()
    {
        // Arrange: MainViewModel with 1 profile, real ProfileHealthService
        var healthService = new ProfileHealthService();
        var viewModel = new MainViewModel(profileHealthService: healthService);

        var profile = CreateProfile("TestProfile", "Profile 1");
        viewModel.Profiles.Add(profile);

        var row = viewModel.ProfileRows.First();
        Assert.Null(row.HealthStatus); // Initially null

        // Act: Execute CheckProfileHealthAsync with that profile
        await viewModel.CheckProfileHealthAsync(row);

        // Assert: HealthStatus updated
        Assert.NotNull(row.HealthStatus);
        Assert.NotNull(row.HealthStatus.Message);
        Assert.NotEqual(HealthLevel.Unknown, row.HealthStatus.Level);
        Assert.NotEqual(default, row.HealthStatus.LastChecked);
    }

    [Fact]
    public async Task CheckProfileHealthAsync_HandlesNullParameter()
    {
        // Arrange
        var healthService = new ProfileHealthService();
        var viewModel = new MainViewModel(profileHealthService: healthService);
        var profile = CreateProfile("Test", "Profile 1");
        viewModel.Profiles.Add(profile);

        var row = viewModel.ProfileRows.First();
        Assert.Null(row.HealthStatus);

        // Act - should not throw
        await viewModel.CheckProfileHealthAsync(null);

        // Assert - HealthStatus remains null (no update happened)
        Assert.Null(row.HealthStatus);
    }

    [Fact]
    public async Task CheckAllProfilesHealthAsync_UpdatesMultipleProfiles()
    {
        // Arrange
        var healthService = new ProfileHealthService();
        var viewModel = new MainViewModel(profileHealthService: healthService);

        for (int i = 1; i <= 5; i++)
        {
            viewModel.Profiles.Add(CreateProfile($"Profile{i}", $"Profile {i}"));
        }

        // Act
        await viewModel.CheckAllProfilesHealthAsync();

        // Assert - all 5 profiles have health status
        Assert.Equal(5, viewModel.ProfileRows.Count);
        Assert.All(viewModel.ProfileRows, row => Assert.NotNull(row.HealthStatus));
    }

    [Fact]
    public async Task CheckProfileHealthAsync_UpdatesOnlySpecifiedProfile()
    {
        // Arrange
        var healthService = new ProfileHealthService();
        var viewModel = new MainViewModel(profileHealthService: healthService);

        viewModel.Profiles.Add(CreateProfile("Profile1", "Profile 1"));
        viewModel.Profiles.Add(CreateProfile("Profile2", "Profile 2"));

        var targetRow = viewModel.ProfileRows[0];
        var otherRow = viewModel.ProfileRows[1];

        // Act - check only first profile
        await viewModel.CheckProfileHealthAsync(targetRow);

        // Assert - only target row is updated
        Assert.NotNull(targetRow.HealthStatus);
        Assert.Null(otherRow.HealthStatus);
    }

    [Fact]
    public void CheckAllProfilesHealthCommand_CanExecute_ReturnsFalseWhenNoProfiles()
    {
        // Arrange
        var viewModel = new MainViewModel();

        // Assert
        Assert.False(viewModel.CheckAllProfilesHealthCommand.CanExecute(null));
    }

    [Fact]
    public void CheckAllProfilesHealthCommand_CanExecute_ReturnsTrueWhenProfilesExist()
    {
        // Arrange
        var viewModel = new MainViewModel();
        viewModel.Profiles.Add(CreateProfile("Test", "Profile 1"));

        // Assert
        Assert.True(viewModel.CheckAllProfilesHealthCommand.CanExecute(null));
    }

    [Fact]
    public void CheckProfileHealthCommand_CanExecute_ReturnsFalseWhenParameterIsNull()
    {
        // Arrange
        var viewModel = new MainViewModel();

        // Assert
        Assert.False(viewModel.CheckProfileHealthCommand.CanExecute(null));
    }

    [Fact]
    public void CheckProfileHealthCommand_CanExecute_ReturnsTrueWhenParameterIsValid()
    {
        // Arrange
        var viewModel = new MainViewModel();
        var profile = CreateProfile("Test", "Profile 1");
        viewModel.Profiles.Add(profile);
        var row = viewModel.ProfileRows.First();

        // Assert
        Assert.True(viewModel.CheckProfileHealthCommand.CanExecute(row));
    }

    private static ChromeProfile CreateProfile(string name, string directoryName) =>
        new(
            ChromeProfile.CreateId("C:\\Chrome\\User Data", directoryName),
            name,
            directoryName,
            "C:\\Chrome\\User Data",
            false);
}
