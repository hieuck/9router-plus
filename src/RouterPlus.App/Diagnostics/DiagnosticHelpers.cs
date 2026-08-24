using System;
using System.Diagnostics;

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
    [Conditional("DEBUG")]
    public static void LogClick(string element, string? details = null)
    {
        DebugLogger.Log(DiagnosticCategories.UI, $"CLICK {element}{(details != null ? $" - {details}" : "")}");
    }

    [Conditional("DEBUG")]
    public static void LogRightClick(string element, string? details = null)
    {
        DebugLogger.Log(DiagnosticCategories.UI, $"RIGHT-CLICK {element}{(details != null ? $" - {details}" : "")}");
    }

    [Conditional("DEBUG")]
    public static void LogDoubleClick(string element, string? details = null)
    {
        DebugLogger.Log(DiagnosticCategories.UI, $"DOUBLE-CLICK {element}{(details != null ? $" - {details}" : "")}");
    }

    [Conditional("DEBUG")]
    public static void LogSelection(string element, string? selectedValue)
    {
        DebugLogger.Log(DiagnosticCategories.UI, $"SELECTION {element} = {selectedValue ?? "null"}");
    }

    [Conditional("DEBUG")]
    public static void LogTextInput(string element, int length)
    {
        DebugLogger.Log(DiagnosticCategories.UI, $"TEXT-INPUT {element} (length: {length})");
    }

    [Conditional("DEBUG")]
    public static void LogContextMenuOpen(string element)
    {
        DebugLogger.Log(DiagnosticCategories.UI, $"CONTEXT-MENU-OPEN {element}");
    }

    [Conditional("DEBUG")]
    public static void LogDialogOpen(string dialogName)
    {
        DebugLogger.Log(DiagnosticCategories.UI, $"DIALOG-OPEN {dialogName}");
    }

    [Conditional("DEBUG")]
    public static void LogDialogClose(string dialogName, bool? result = null)
    {
        var resultStr = result.HasValue ? (result.Value ? "OK" : "Cancel") : "Closed";
        DebugLogger.Log(DiagnosticCategories.UI, $"DIALOG-CLOSE {dialogName} ({resultStr})");
    }
}

/// <summary>
/// ViewModel operation diagnostics helper
/// </summary>
public static class ViewModelLogger
{
    [Conditional("DEBUG")]
    public static void LogPropertyChanged(string viewModel, string propertyName)
    {
        DebugLogger.Log(DiagnosticCategories.ViewModel, $"{viewModel}.{propertyName} changed");
    }

    [Conditional("DEBUG")]
    public static void LogCommandExecute(string viewModel, string commandName, string? parameter = null)
    {
        var paramStr = parameter != null ? $" (param: {parameter})" : "";
        DebugLogger.Log(DiagnosticCategories.Commands, $"{viewModel}.{commandName} executed{paramStr}");
    }

    [Conditional("DEBUG")]
    public static void LogCommandCanExecuteChanged(string viewModel, string commandName, bool canExecute)
    {
        DebugLogger.Log(DiagnosticCategories.Commands, $"{viewModel}.{commandName} CanExecute = {canExecute}");
    }

    [Conditional("DEBUG")]
    public static void LogDataLoad(string viewModel, string dataType, int count)
    {
        DebugLogger.Log(DiagnosticCategories.ViewModel, $"{viewModel} loaded {count} {dataType}");
    }
}

/// <summary>
/// Chrome operations diagnostics helper
/// </summary>
public static class ChromeLogger
{
    [Conditional("DEBUG")]
    public static void LogProfileScan(int profileCount, long elapsedMs)
    {
        DebugLogger.Log(DiagnosticCategories.Chrome, $"Profile scan completed: {profileCount} profiles in {elapsedMs}ms");
    }

    [Conditional("DEBUG")]
    public static void LogProfileLaunch(string profileName)
    {
        DebugLogger.Log(DiagnosticCategories.Chrome, $"Launching profile: {profileName}");
    }

    [Conditional("DEBUG")]
    public static void LogProfileLaunchSuccess(string profileName, long elapsedMs)
    {
        DebugLogger.Log(DiagnosticCategories.Chrome, $"Profile launched successfully: {profileName} ({elapsedMs}ms)");
    }

    [Conditional("DEBUG")]
    public static void LogProfileLaunchFailed(string profileName, string reason)
    {
        DebugLogger.LogError(DiagnosticCategories.Chrome, $"Profile launch failed: {profileName} - {reason}");
    }
}
