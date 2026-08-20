using System.Xml.Linq;

namespace RouterPlus.Core.Tests;

public sealed class ThemeTemplateTests
{
    [Fact]
    public void ToggleButton_template_forwards_foreground_to_content_presenter()
    {
        var themeDocument = XDocument.Load(FindRepositoryFile(Path.Combine("src", "RouterPlus.App", "Styles", "Theme.xaml")));
        var toggleButtonStyle = themeDocument
            .Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "ToggleButton");
        var contentPresenter = toggleButtonStyle
            .Descendants()
            .Single(element => element.Name.LocalName == "ContentPresenter");
        var foregroundBinding = contentPresenter
            .Attributes()
            .SingleOrDefault(attribute => attribute.Name.ToString() == "TextElement.Foreground");

        Assert.NotNull(foregroundBinding);
        Assert.Equal("{TemplateBinding Foreground}", foregroundBinding!.Value);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RouterPlus.sln")))
            {
                return Path.Combine(directory.FullName, relativePath);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the RouterPlus repository root.");
    }
}
