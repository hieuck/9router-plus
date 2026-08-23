using System;
using System.Windows;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();

        // Keep the application alive while the setup wizard is the only open window.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Show wizard if first-time user (no Chrome paths configured)
        if (string.IsNullOrWhiteSpace(settings.ChromeExecutablePath) ||
            string.IsNullOrWhiteSpace(settings.ChromeUserDataDirectory))
        {
            var wizard = new WelcomeWizardWindow(settingsStore);
            var result = wizard.ShowDialog();

            // If user completed wizard, reload settings
            if (result == true)
            {
                settings = settingsStore.Load();
            }
            // If user skipped, continue with empty settings (they can configure later)

        }

        // Always show main window
        try
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Error opening main window:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
