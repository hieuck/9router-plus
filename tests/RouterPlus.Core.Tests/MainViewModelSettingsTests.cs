using RouterPlus.App.ViewModels;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelSettingsTests
{
    [Fact]
    public void New_view_model_starts_with_light_theme()
    {
        var viewModel = new MainViewModel();

        Assert.True(viewModel.UseLightTheme);
    }

    [Fact]
    public async Task SaveWindowPlacement_preserves_unsaved_settings_and_updates_placement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "settings.json");

        try
        {
            var store = new SettingsStore(filePath);
            await store.SaveAsync(new RouterSettings(DashboardBaseUrl: "http://saved.example"));

            var viewModel = new MainViewModel(store)
            {
                DashboardBaseUrl = "http://unsaved.example"
            };

            await viewModel.SaveWindowPlacementAsync(240d, 130d, 1320d, 840d);

            var settings = await store.LoadAsync();
            Assert.Equal("http://saved.example", settings.DashboardBaseUrl);
            Assert.Equal(240d, settings.WindowLeft);
            Assert.Equal(130d, settings.WindowTop);
            Assert.Equal(1320d, settings.WindowWidth);
            Assert.Equal(840d, settings.WindowHeight);
            Assert.Equal(
                new MainViewModel.WindowPlacement(240d, 130d, 1320d, 840d),
                viewModel.SavedWindowPlacement);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

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
    public void Missing_chrome_executable_blocks_save_with_specific_validation_message()
    {
        var viewModel = new MainViewModel
        {
            ChromeExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "chrome.exe")
        };

        Assert.True(viewModel.HasSettingsValidationError);
        Assert.Equal("Không tìm thấy file Chrome đã chọn.", viewModel.SettingsValidationMessage);
        Assert.False(viewModel.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void Missing_chrome_user_data_directory_blocks_save_with_specific_validation_message()
    {
        var viewModel = new MainViewModel
        {
            ChromeUserDataDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        };

        Assert.True(viewModel.HasSettingsValidationError);
        Assert.Equal("Không tìm thấy thư mục dữ liệu Chrome đã chọn.", viewModel.SettingsValidationMessage);
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
