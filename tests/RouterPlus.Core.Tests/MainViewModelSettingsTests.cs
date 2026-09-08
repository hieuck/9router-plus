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

    [Fact]
    public void Font_scale_is_clamped_and_reports_percentage_label()
    {
        // Arrange
        var viewModel = new MainViewModel();

        // Act
        viewModel.FontScale = 2d;

        // Assert
        Assert.Equal(1.4d, viewModel.FontScale);
        Assert.Equal("140%", viewModel.FontScaleLabel);
        Assert.True(viewModel.HasUnsavedSettings);

        // Act
        viewModel.FontScale = 0.5d;

        // Assert
        Assert.Equal(0.9d, viewModel.FontScale);
        Assert.Equal("90%", viewModel.FontScaleLabel);
    }

    [Fact]
    public void Theme_and_section_commands_update_their_view_state()
    {
        // Arrange
        var viewModel = new MainViewModel();

        // Act
        viewModel.UseLightTheme = false;
        viewModel.IsAppearanceSectionExpanded = false;
        viewModel.IsDashboardSectionExpanded = false;
        viewModel.IsChromeSectionExpanded = false;
        viewModel.IsSettingsExpanded = true;
        viewModel.IsProfileSidebarCollapsed = true;

        // Assert
        Assert.False(viewModel.UseLightTheme);
        Assert.True(viewModel.HasUnsavedSettings);
        Assert.False(viewModel.IsAppearanceSectionExpanded);
        Assert.False(viewModel.IsDashboardSectionExpanded);
        Assert.False(viewModel.IsChromeSectionExpanded);
        Assert.True(viewModel.IsSettingsExpanded);
        Assert.True(viewModel.IsProfileSidebarCollapsed);

        // Act
        viewModel.ToggleAppearanceSectionCommand.Execute(null);
        viewModel.ToggleDashboardSectionCommand.Execute(null);
        viewModel.ToggleChromeSectionCommand.Execute(null);

        // Assert
        Assert.True(viewModel.IsAppearanceSectionExpanded);
        Assert.True(viewModel.IsDashboardSectionExpanded);
        Assert.True(viewModel.IsChromeSectionExpanded);
    }

    [Fact]
    public void Valid_paths_and_dashboard_url_pass_settings_validation()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        var executable = Path.Combine(directory, "chrome.exe");
        Directory.CreateDirectory(directory);
        File.WriteAllText(executable, string.Empty);

        try
        {
            var viewModel = new MainViewModel
            {
                DashboardBaseUrl = "https://dashboard.example.test/base",
                ChromeExecutablePath = executable,
                ChromeUserDataDirectory = directory
            };

            // Assert
            Assert.True(viewModel.IsDashboardUrlValid);
            Assert.True(viewModel.IsChromeExecutableValid);
            Assert.True(viewModel.IsChromeUserDataValid);
            Assert.False(viewModel.HasSettingsValidationError);
            Assert.True(viewModel.SaveSettingsCommand.CanExecute(null));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Clear_setting_commands_restore_default_values()
    {
        // Arrange
        var viewModel = new MainViewModel
        {
            DashboardBaseUrl = "https://dashboard.example.test",
            ChromeExecutablePath = "chrome.exe",
            ChromeUserDataDirectory = "C:\\\\Chrome"
        };

        // Act
        viewModel.ClearDashboardUrlCommand.Execute(null);
        viewModel.ClearChromeExecutableCommand.Execute(null);
        viewModel.ClearChromeUserDataCommand.Execute(null);
        await Task.Delay(50);

        // Assert
        Assert.Equal("http://localhost:20128", viewModel.DashboardBaseUrl);
        Assert.Equal(string.Empty, viewModel.ChromeExecutablePath);
        Assert.Equal(string.Empty, viewModel.ChromeUserDataDirectory);
    }
}
