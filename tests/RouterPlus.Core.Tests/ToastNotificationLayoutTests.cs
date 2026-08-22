using System.Xml.Linq;

namespace RouterPlus.Core.Tests;

public sealed class ToastNotificationLayoutTests
{
    [Fact]
    public void Toast_overlay_is_top_right_of_content_area()
    {
        var xamlPath = FindRepositoryFile("src", "RouterPlus.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var toastBorder = document
            .Descendants(presentation + "Border")
            .Single(border => border
                .Descendants()
                .Any(element => string.Equals(
                    (string?)element.Attribute("Binding"),
                    "{Binding CurrentToast.IsVisible}",
                    StringComparison.Ordinal)));

        Assert.Equal("1", (string?)toastBorder.Attribute("Grid.Row"));
        Assert.Equal("1", (string?)toastBorder.Attribute("Grid.Column"));
        Assert.Equal("Top", (string?)toastBorder.Attribute("VerticalAlignment"));
        Assert.Equal("Right", (string?)toastBorder.Attribute("HorizontalAlignment"));
        Assert.Equal("0,14,26,0", (string?)toastBorder.Attribute("Margin"));
        Assert.Null(toastBorder.Attribute("Grid.RowSpan"));
        Assert.Null(toastBorder.Attribute("Grid.ColumnSpan"));
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(parts)}");
    }
}
