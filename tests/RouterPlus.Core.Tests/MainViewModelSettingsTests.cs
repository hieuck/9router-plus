using RouterPlus.App.ViewModels;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelSettingsTests
{
    [Fact]
    public void Settings_start_valid_and_saved()
    {
        var viewModel = new MainViewModel();

        Assert.False(viewModel.HasUnsavedSettings);
        Assert.False(viewModel.HasSettingsValidationError);
        Assert.Equal("Đã lưu", viewModel.SettingsStatusText);
        Assert.True(viewModel.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void Invalid_dashboard_url_blocks_save_and_reports_validation_error()
    {
        var viewModel = new MainViewModel
        {
            DashboardBaseUrl = "not a url"
        };

        Assert.True(viewModel.HasUnsavedSettings);
        Assert.True(viewModel.HasSettingsValidationError);
        Assert.Equal("Nhập URL dashboard hợp lệ.", viewModel.SettingsStatusText);
        Assert.False(viewModel.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void Valid_setting_change_reports_unsaved_state_and_allows_save()
    {
        var viewModel = new MainViewModel
        {
            DashboardBaseUrl = "http://localhost:20129"
        };

        Assert.True(viewModel.HasUnsavedSettings);
        Assert.False(viewModel.HasSettingsValidationError);
        Assert.Equal("Có thay đổi chưa lưu", viewModel.SettingsStatusText);
        Assert.True(viewModel.SaveSettingsCommand.CanExecute(null));
    }
}
