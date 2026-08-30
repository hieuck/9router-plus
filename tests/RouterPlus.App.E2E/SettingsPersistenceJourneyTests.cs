using FlaUI.Core.Tools;

namespace RouterPlus.App.E2E;

public sealed class SettingsPersistenceJourneyTests
{
    [Fact]
    public async Task Invalid_dashboard_url_is_visible_and_save_is_disabled()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);
        var driver = new AppDriver(app);

        try
        {
            driver.OpenSettings();
            driver.SetDashboardUrl("not a url");

            Retry.WhileFalse(
                () => driver.ReadSettingsStatus().Contains("URL dashboard", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(3));

            Assert.Contains("URL dashboard", driver.ReadSettingsStatus(), StringComparison.OrdinalIgnoreCase);
            Assert.False(driver.IsSaveSettingsEnabled());
        }
        catch
        {
            await app.Instrumentation.SaveFailureSnapshotAsync(app, nameof(Invalid_dashboard_url_is_visible_and_save_is_disabled));
            throw;
        }
    }

    [Fact]
    public async Task Dashboard_url_survives_save_and_application_restart()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var expectedUrl = "http://127.0.0.1:20129";

        await using (var app = await AppProcess.StartAsync(environment))
        {
            var driver = new AppDriver(app);
            try
            {
                driver.OpenSettings();
                driver.SetDashboardUrl(expectedUrl);
                Retry.WhileFalse(
                    () => driver.IsSaveSettingsEnabled(),
                    TimeSpan.FromSeconds(3));
                Assert.True(driver.IsSaveSettingsEnabled());

                driver.SaveSettings();
                Retry.WhileFalse(
                    () => driver.ReadSettingsStatus().Contains("Đã lưu", StringComparison.OrdinalIgnoreCase),
                    TimeSpan.FromSeconds(5));
                Assert.Contains("Đã lưu", driver.ReadSettingsStatus(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                await app.Instrumentation.SaveFailureSnapshotAsync(app, nameof(Dashboard_url_survives_save_and_application_restart));
                throw;
            }
        }

        await using (var restartedApp = await AppProcess.StartAsync(environment))
        {
            var driver = new AppDriver(restartedApp);
            try
            {
                driver.OpenSettings();
                Retry.WhileFalse(
                    () => string.Equals(driver.ReadDashboardUrl(), expectedUrl, StringComparison.Ordinal),
                    TimeSpan.FromSeconds(3));
                Assert.Equal(expectedUrl, driver.ReadDashboardUrl());
            }
            catch
            {
                await restartedApp.Instrumentation.SaveFailureSnapshotAsync(restartedApp, nameof(Dashboard_url_survives_save_and_application_restart) + "-restart");
                throw;
            }
        }
    }
}
