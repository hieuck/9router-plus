using System.Windows;
using System.Windows.Input;
using RouterPlus.App.ViewModels;
using RouterPlus.Infrastructure.Observability;

namespace RouterPlus.App.Views;

/// <summary>
/// Interaction logic for DiagnosticsWindow.xaml
/// </summary>
public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        InitializeComponent();
        DataContext = new DiagnosticsViewModel();
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Copy event on double-click
        if (DataContext is DiagnosticsViewModel vm && vm.SelectedEvent != null)
        {
            vm.CopyEventCommand.Execute(null);
        }
    }
}
