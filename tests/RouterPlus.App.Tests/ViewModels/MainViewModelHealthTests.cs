using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;

namespace RouterPlus.App.Tests.ViewModels;

public sealed class MainViewModelHealthTests
{
    [Fact]
    public void CheckAllProfilesHealthCommand_IsNotNull()
    {
        // Arrange
        var viewModel = new MainViewModel();

        // Assert
        Assert.NotNull(viewModel.CheckAllProfilesHealthCommand);
    }

    [Fact]
    public void CheckProfileHealthCommand_IsNotNull()
    {
        // Arrange
        var viewModel = new MainViewModel();

        // Assert
        Assert.NotNull(viewModel.CheckProfileHealthCommand);
    }

    [Fact]
    public void CheckAllProfilesHealthCommand_CanExecute_ReturnsFalseWhenNoProfiles()
    {
        // Arrange
        var viewModel = new MainViewModel();

        // Assert
        Assert.Empty(viewModel.ProfileRows);
        Assert.False(viewModel.CheckAllProfilesHealthCommand.CanExecute(null));
    }

    [Fact]
    public void CheckAllProfilesHealthCommand_CanExecute_ReturnsTrueWhenProfilesExist()
    {
        // Arrange
        var viewModel = new MainViewModel();
        var profile = CreateProfile("Test", "Profile 1");

        // Add to both Profiles and ProfileRows (mimics RefreshProfiles behavior)
        viewModel.Profiles.Add(profile);
        var row = new ProfileRowViewModel(profile, viewModel.Providers);
        viewModel.ProfileRows.Add(row);

        // Assert
        Assert.Single(viewModel.ProfileRows);
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

        // Add to both Profiles and ProfileRows (mimics RefreshProfiles behavior)
        viewModel.Profiles.Add(profile);
        var row = new ProfileRowViewModel(profile, viewModel.Providers);
        viewModel.ProfileRows.Add(row);

        // Assert
        Assert.Single(viewModel.ProfileRows);
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
