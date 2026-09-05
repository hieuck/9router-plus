using System;
using System.Windows;
using RouterPlus.App.Diagnostics;
using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Observability;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App;

public partial class App : System.Windows.Application
{
    private SessionManager? _sessionManager;

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

        // Initialize observability system FIRST (before anything else)
        InitializeObservability();

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

    private void InitializeObservability()
    {
        try
        {
            DebugLogger.Log(DiagnosticCategories.Startup, "Initializing observability system");

            // Check if observability is enabled
            var settings = ObservabilitySettings.Load();
            if (!settings.EnableLogging && !settings.EnableMetrics && !settings.EnableSnapshots)
            {
                DebugLogger.Log(DiagnosticCategories.Startup, "Observability disabled in settings - skipping initialization");
                return;
            }

            // Create paths and session manager
            var paths = new ObservabilityPaths();
            DebugLogger.Log(DiagnosticCategories.Startup, $"Observability root: {paths.RootDirectory}");

            _sessionManager = new SessionManager(paths);
            DebugLogger.Log(DiagnosticCategories.Startup, $"Session ID: {_sessionManager.SessionId}");

            // Initialize session directory and metadata
            try
            {
                _sessionManager.Initialize();
                DebugLogger.Log(DiagnosticCategories.Startup, "Session directory created");
            }
            catch (Exception ex)
            {
                DebugLogger.LogError(DiagnosticCategories.Startup, "Initialize failed", ex);
                throw;
            }

            // Clean up old sessions in background (don't block startup)
            System.Threading.Tasks.Task.Run(() => _sessionManager.CleanupOldSessions());

            // Create and set writer
            try
            {
                var writer = new JsonLinesWriter(paths, _sessionManager.SessionId);
                DebugLogger.Log(DiagnosticCategories.Startup, "JsonLinesWriter created");

                ObservabilityHub.Instance.SetWriter(writer);
                DebugLogger.Log(DiagnosticCategories.Startup, "ObservabilityHub writer set");
            }
            catch (Exception ex)
            {
                DebugLogger.LogError(DiagnosticCategories.Startup, "Writer setup failed", ex);
                throw;
            }

            // Log first event
            try
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Startup",
                    "AppStarted",
                    "Application starting",
                    new
                    {
                        version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                        os = Environment.OSVersion.ToString(),
                        dotnet_version = Environment.Version.ToString(),
                        session_id = _sessionManager.SessionId
                    });
                DebugLogger.Log(DiagnosticCategories.Startup, "ObservabilityHub first event logged");
            }
            catch (Exception ex)
            {
                DebugLogger.LogError(DiagnosticCategories.Startup, "First event log failed", ex);
                throw;
            }
        }
        catch (Exception ex)
        {
            // Never crash app due to observability failure
            DebugLogger.LogError(DiagnosticCategories.Startup, "Observability initialization failed", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Log shutdown event
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Shutdown",
                "AppExiting",
                "Application exiting",
                new { exit_code = e.ApplicationExitCode });

            // Dispose hub (flushes pending events)
            ObservabilityHub.Instance.Dispose();

            // Finalize session metadata
            _sessionManager?.FinalizeAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Best effort cleanup
        }
        finally
        {
            base.OnExit(e);
        }
    }
}
