using System;
using System.Windows;
using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Observability;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.App.Testing;
using RouterPlus.App.Diagnostics;

namespace RouterPlus.App;

public partial class App : System.Windows.Application
{
    private SessionManager? _sessionManager;

    /// <summary>
    /// Gets the current session ID for observability (null if observability disabled)
    /// </summary>
    public static string? CurrentSessionId { get; private set; }

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
        base.OnStartup(e);
        HarnessEnvironment.Trace("WPF base startup completed");

        // Initialize observability system FIRST (before anything else)
        InitializeObservability();

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Application",
            "StartupBegan",
            "Application startup began",
            new { });

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

        var setupRequired = string.IsNullOrWhiteSpace(settings.ChromeExecutablePath) ||
                           string.IsNullOrWhiteSpace(settings.ChromeUserDataDirectory);
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Application",
            "SettingsLoaded",
            "Initial settings loaded",
            new { setup_required = setupRequired });

        // Keep the application alive while the setup wizard is the only open window.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Show wizard if first-time user (no Chrome paths configured).
        // E2E tests bypass it so they can attach to the main window directly.
        var skipSetupWizard = string.Equals(
            Environment.GetEnvironmentVariable("ROUTERPLUS_SKIP_SETUP_WIZARD"),
            "1",
            StringComparison.Ordinal);
        if (!HarnessEnvironment.IsEnabled &&
            !skipSetupWizard &&
            (string.IsNullOrWhiteSpace(settings.ChromeExecutablePath) ||
             string.IsNullOrWhiteSpace(settings.ChromeUserDataDirectory)))
        {
            var wizard = new WelcomeWizardWindow(settingsStore);
            var result = wizard.ShowDialog();

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Application",
                "SetupWizardClosed",
                "Setup wizard closed",
                new { result = result?.ToString() ?? "null" });

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
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Application",
                "CreatingMainWindow",
                "Creating main window",
                new { });

            HarnessEnvironment.Trace("Creating main window");
            var mainWindow = new MainWindow();
            HarnessEnvironment.Trace("Main window constructed");
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            HarnessEnvironment.Trace("Main window shown");

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Application",
                "MainWindowShown",
                "Main window shown",
                new { });
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "Application",
                "MainWindowStartupFailed",
                $"Main window startup failed: {ex.Message}",
                new { error = ex.Message, error_type = ex.GetType().Name, stack_trace = ex.StackTrace });

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
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Observability",
                "InitializationStarted",
                "Initializing observability system",
                new { });

            // Check if observability is enabled
            var settings = ObservabilitySettings.Load();
            if (!settings.EnableLogging && !settings.EnableMetrics && !settings.EnableSnapshots)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Observability",
                    "DisabledInSettings",
                    "Observability disabled in settings - skipping initialization",
                    new { });
                return;
            }

            // Create paths and session manager
            var paths = new ObservabilityPaths();
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Observability",
                "PathsCreated",
                "Observability paths initialized",
                new { root_directory = paths.RootDirectory });

            _sessionManager = new SessionManager(paths);
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Observability",
                "SessionCreated",
                "Session manager created",
                new { session_id = _sessionManager.SessionId });

            // Store session ID for diagnostics access
            CurrentSessionId = _sessionManager.SessionId;

            // Initialize session directory and metadata
            try
            {
                _sessionManager.Initialize();
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Observability",
                    "SessionInitialized",
                    "Session directory created",
                    new { });
            }
            catch (Exception ex)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Error,
                    "Observability",
                    "SessionInitializeFailed",
                    $"Initialize failed: {ex.Message}",
                    new { error = ex.Message, error_type = ex.GetType().Name });
                throw;
            }

            // Clean up old sessions in background (don't block startup)
            System.Threading.Tasks.Task.Run(() => _sessionManager.CleanupOldSessions());

            // Create and set writer
            try
            {
                var writer = new JsonLinesWriter(paths, _sessionManager.SessionId);
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Observability",
                    "WriterCreated",
                    "JsonLinesWriter created",
                    new { });

                ObservabilityHub.Instance.SetWriter(writer);
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Observability",
                    "WriterSet",
                    "ObservabilityHub writer set",
                    new { });
            }
            catch (Exception ex)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Error,
                    "Observability",
                    "WriterSetupFailed",
                    $"Writer setup failed: {ex.Message}",
                    new { error = ex.Message, error_type = ex.GetType().Name });
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
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Observability",
                    "FirstEventLogged",
                    "ObservabilityHub first event logged",
                    new { });
            }
            catch (Exception ex)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Error,
                    "Observability",
                    "FirstEventFailed",
                    $"First event log failed: {ex.Message}",
                    new { error = ex.Message, error_type = ex.GetType().Name });
                throw;
            }
        }
        catch (Exception ex)
        {
            // Never crash app due to observability failure
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "Observability",
                "InitializationFailed",
                $"Observability initialization failed: {ex.Message}",
                new { error = ex.Message, error_type = ex.GetType().Name, stack_trace = ex.StackTrace });
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
