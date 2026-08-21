using System.Windows;
using RouterPlus.App.ViewModels;

namespace RouterPlus.App;

public partial class AboutWindow : Window
{
    private readonly IExternalLinkLauncher _linkLauncher = new ShellLinkLauncher();

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = new AboutViewModel();
    }

    private void Repository_Click(object sender, RoutedEventArgs e) => Open(ApplicationLinks.RepositoryUri);

    private void Help_Click(object sender, RoutedEventArgs e) => Open(ApplicationLinks.HelpUri);

    private void Security_Click(object sender, RoutedEventArgs e) => Open(ApplicationLinks.SecurityUri);

    private void Release_Click(object sender, RoutedEventArgs e) => Open(ApplicationLinks.ReleaseUri);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Open(Uri uri)
    {
        try
        {
            _linkLauncher.Open(uri);
        }
        catch
        {
            System.Windows.MessageBox.Show(
                this,
                "Không thể mở liên kết. Hãy thử lại từ trình duyệt.",
                "9Router Profile Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
