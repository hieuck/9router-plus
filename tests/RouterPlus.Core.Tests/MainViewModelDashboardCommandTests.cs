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

    [Fact]
    public async Task Manual_status_sync_appends_a_log_entry_even_when_result_is_unchanged()
    {
        var viewModel = new MainViewModel();

        await viewModel.RefreshConnectionStatusesAsync();
        var firstLog = viewModel.LogText;

        await viewModel.RefreshConnectionStatusesAsync();
        var secondLog = viewModel.LogText;

        Assert.Contains("[SYNC]", secondLog, StringComparison.Ordinal);
        Assert.True(secondLog.Length > firstLog.Length);
    }
}
