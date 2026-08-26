using System;
using System.Windows;
using RouterPlus.App.Diagnostics;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Debug-only path: ROUTERPLUS_DEBUG_AUTOLOGIN=1 runs the production
        // Google Auto Login automation against the configured profile and
        // exits, so the live E2E fix can be validated without UI automation
        // flakiness. The flow uses the same vault/credential/automation
        // delegates as the dialog. Never logs secrets.
        var debugAutoLogin = System.Environment.GetEnvironmentVariable("ROUTERPLUS_DEBUG_AUTOLOGIN");
        if (!string.IsNullOrEmpty(debugAutoLogin) && debugAutoLogin != "false")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RouterPlus.App.Diagnostics.DebugAutoLoginRunner.RunAsync();
            return;
        }

        HarnessEnvironment.Trace("OnStartup entered");
        DebugLogger.LogSeparator(DiagnosticCategories.Startup);
        DebugLogger.Log(DiagnosticCategories.Startup, "Application startup began");
        base.OnStartup(e);
        HarnessEnvironment.Trace("WPF base startup completed");

        var settingsStore = HarnessEnvironment.CreateSettingsStore();
        HarnessEnvironment.Trace("Settings store created");
        var settings = HarnessEnvironment.IsEnabled
            ? HarnessEnvironment.CreateSettings()
            : settingsStore.Load();
        HarnessEnvironment.Trace("Settings loaded");
        if (HarnessEnvironment.IsEnabled)
        {
            HarnessEnvironment.Trace("Using synthetic harness settings");
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
            HarnessEnvironment.Trace("Creating main window");
            var mainWindow = new MainWindow();
            HarnessEnvironment.Trace("Main window constructed");
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            HarnessEnvironment.Trace("Main window shown");
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
