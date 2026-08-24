using System;
using System.Windows;
using RouterPlus.App.Diagnostics;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DebugLogger.LogSeparator(DiagnosticCategories.Startup);
        DebugLogger.Log(DiagnosticCategories.Startup, "Application startup began");
        base.OnStartup(e);

        var settingsStore = HarnessEnvironment.CreateSettingsStore();
        var settings = HarnessEnvironment.IsEnabled
            ? HarnessEnvironment.CreateSettings()
            : settingsStore.Load();
        if (HarnessEnvironment.IsEnabled)
        {
            settingsStore.SaveAsync(settings).GetAwaiter().GetResult();
        }
        DebugLogger.Log(DiagnosticCategories.Startup, $"Initial settings loaded; setup required: {string.IsNullOrWhiteSpace(settings.ChromeExecutablePath) || string.IsNullOrWhiteSpace(settings.ChromeUserDataDirectory)}");

        // Keep the application alive while the setup wizard is the only open window.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Show wizard if first-time user (no Chrome paths configured)
        if (!HarnessEnvironment.IsEnabled &&
            (string.IsNullOrWhiteSpace(settings.ChromeExecutablePath) ||
             string.IsNullOrWhiteSpace(settings.ChromeUserDataDirectory)))
        {
            var wizard = new WelcomeWizardWindow(settingsStore);
            var result = wizard.ShowDialog();
            DebugLogger.Log(DiagnosticCategories.Startup, $"Setup wizard closed with result: {result}");

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
            DebugLogger.Log(DiagnosticCategories.Startup, "Creating main window");
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            DebugLogger.Log(DiagnosticCategories.Startup, "Main window shown");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError(DiagnosticCategories.Startup, "Main window startup failed", ex);
            System.Windows.MessageBox.Show(
                $"Error opening main window:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
