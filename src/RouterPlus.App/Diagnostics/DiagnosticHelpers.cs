using System;
using System.Diagnostics;
using RouterPlus.Core.Observability;

namespace RouterPlus.App.Diagnostics;

/// <summary>
/// Diagnostic logging categories for different app subsystems
/// </summary>
public static class DiagnosticCategories
{
    public const string UI = "UI";
    public const string UX = "UX";
    public const string ViewModel = "ViewModel";
    public const string Navigation = "Navigation";
    public const string Commands = "Commands";
    public const string DataBinding = "DataBinding";
    public const string Chrome = "Chrome";
    public const string Providers = "Providers";
    public const string Storage = "Storage";
    public const string Security = "Security";
    public const string Performance = "Performance";
    public const string Startup = "Startup";
    public const string Updates = "Updates";
}

/// <summary>
/// UI event diagnostics helper
/// </summary>
public static class UIEventLogger
{
    public static void LogClick(string element, string? details = null)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "UI",
            "Click",
            $"CLICK {element}",
            new { element, details });
    }

    public static void LogRightClick(string element, string? details = null)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "UI",
            "RightClick",
            $"RIGHT-CLICK {element}",
            new { element, details });
    }

    public static void LogDoubleClick(string element, string? details = null)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "UI",
            "DoubleClick",
            $"DOUBLE-CLICK {element}",
            new { element, details });
    }

    public static void LogSelection(string element, string? selectedValue)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "UI",
            "Selection",
            $"SELECTION {element}",
            new { element, selected_value = selectedValue ?? "null" });
    }

    public static void LogTextInput(string element, int length)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "UI",
            "TextInput",
            $"TEXT-INPUT {element}",
            new { element, length });
    }

    public static void LogContextMenuOpen(string element)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "UI",
            "ContextMenuOpen",
            $"CONTEXT-MENU-OPEN {element}",
            new { element });
    }

    public static void LogDialogOpen(string dialogName)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "UI",
            "DialogOpen",
            $"DIALOG-OPEN {dialogName}",
            new { dialog_name = dialogName });
    }

    public static void LogDialogClose(string dialogName, bool? result = null)
    {
        var resultStr = result.HasValue ? (result.Value ? "OK" : "Cancel") : "Closed";
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "UI",
            "DialogClose",
            $"DIALOG-CLOSE {dialogName}",
            new { dialog_name = dialogName, result = resultStr });
    }
}

/// <summary>
/// ViewModel operation diagnostics helper
/// </summary>
public static class ViewModelLogger
{
    public static void LogPropertyChanged(string viewModel, string propertyName)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "ViewModel",
            "PropertyChanged",
            $"{viewModel}.{propertyName} changed",
            new { view_model = viewModel, property_name = propertyName });
    }

    public static void LogCommandExecute(string viewModel, string commandName, string? parameter = null)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "Commands",
            "CommandExecute",
            $"{viewModel}.{commandName} executed",
            new { view_model = viewModel, command_name = commandName, parameter });
    }

    public static void LogCommandCanExecuteChanged(string viewModel, string commandName, bool canExecute)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "Commands",
            "CommandCanExecuteChanged",
            $"{viewModel}.{commandName} CanExecute changed",
            new { view_model = viewModel, command_name = commandName, can_execute = canExecute });
    }

    public static void LogDataLoad(string viewModel, string dataType, int count)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "ViewModel",
            "DataLoaded",
            $"{viewModel} loaded {dataType}",
            new { view_model = viewModel, data_type = dataType, count });
    }
}

/// <summary>
/// Chrome operations diagnostics helper
/// </summary>
public static class ChromeLogger
{
    public static void LogProfileScan(int profileCount, long elapsedMs)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Chrome",
            "ProfileScanCompleted",
            "Profile scan completed",
            new { profile_count = profileCount, elapsed_ms = elapsedMs });
    }

    public static void LogProfileLaunch(string profileName)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Chrome",
            "ProfileLaunchStarted",
            "Launching profile",
            new { profile_name = profileName });
    }

    public static void LogProfileLaunchSuccess(string profileName, long elapsedMs)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Chrome",
            "ProfileLaunchSuccess",
            "Profile launched successfully",
            new { profile_name = profileName, elapsed_ms = elapsedMs });
    }

    public static void LogProfileLaunchFailed(string profileName, string reason)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Error,
            "Chrome",
            "ProfileLaunchFailed",
            $"Profile launch failed: {reason}",
            new { profile_name = profileName, reason });
    }
}
