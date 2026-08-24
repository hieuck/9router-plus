using System.Windows.Input;
using RouterPlus.App.Diagnostics;

namespace RouterPlus.App.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Commands, "AsyncRelayCommand.Execute");
        if (!CanExecute(parameter))
        {
            DebugLogger.Log(DiagnosticCategories.Commands, "Async command skipped because CanExecute returned false");
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError(DiagnosticCategories.Commands, "Async command failed", ex);
            // Handle unhandled exceptions from async commands to prevent app crash
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Lỗi không mong đợi:\n\n{ex.Message}",
                    "Lỗi",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }
}

public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T, Task> _execute;
    private readonly Func<T, bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) =>
        !_isRunning && parameter is T value && (_canExecute?.Invoke(value) ?? true);

    public async void Execute(object? parameter)
    {
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Commands, "AsyncRelayCommand<T>.Execute");
        if (parameter is not T value || !CanExecute(parameter))
        {
            DebugLogger.Log(DiagnosticCategories.Commands, "Generic async command skipped because parameter or CanExecute was invalid");
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(value);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError(DiagnosticCategories.Commands, "Generic async command failed", ex);
            // Handle unhandled exceptions from async commands to prevent app crash
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Lỗi không mong đợi:\n\n{ex.Message}",
                    "Lỗi",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }
}
