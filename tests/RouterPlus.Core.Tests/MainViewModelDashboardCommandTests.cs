using RouterPlus.App.ViewModels;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelDashboardCommandTests
{
    [Fact]
    public void Dashboard_command_is_disabled_until_a_profile_is_selected()
    {
        var viewModel = new MainViewModel();

        Assert.False(viewModel.OpenProviderDashboardCommand.CanExecute(ProviderKind.Kiro));
    }
}
