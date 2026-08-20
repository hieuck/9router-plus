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

    [Fact]
    public void ToggleButton_checked_state_uses_accent_content_foreground()
    {
        var themeDocument = XDocument.Load(FindRepositoryFile(Path.Combine("src", "RouterPlus.App", "Styles", "Theme.xaml")));
        var toggleButtonStyle = themeDocument
            .Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "ToggleButton");
        var styleTriggers = toggleButtonStyle
            .Elements()
            .Single(element => element.Name.LocalName == "Style.Triggers");
        var checkedTrigger = styleTriggers
            .Elements()
            .Single(element => element.Name.LocalName == "Trigger"
                && (string?)element.Attribute("Property") == "IsChecked"
                && (string?)element.Attribute("Value") == "True");
        var foregroundSetter = checkedTrigger
            .Elements()
            .SingleOrDefault(element => element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "Foreground");

        Assert.NotNull(foregroundSetter);
        Assert.Equal("{DynamicResource AccentContentBrush}", foregroundSetter!.Attribute("Value")?.Value);
    }

    [Fact]
    public void Settings_toggle_labels_define_explicit_accent_content_foreground()
    {
        var mainWindowDocument = XDocument.Load(FindRepositoryFile(Path.Combine("src", "RouterPlus.App", "MainWindow.xaml")));
        var settingsLabels = mainWindowDocument
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock"
                && ((string?)element.Attribute("Text") == "Xong" || (string?)element.Attribute("Text") == "×"))
            .ToArray();

        Assert.Equal(2, settingsLabels.Length);
        Assert.All(settingsLabels, label =>
        {
            Assert.Contains(label.Ancestors(), ancestor => ancestor.Name.LocalName == "ToggleButton");
            Assert.Equal("{DynamicResource AccentContentBrush}", label.Attribute("Foreground")?.Value);
        });
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
