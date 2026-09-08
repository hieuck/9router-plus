using System.Windows;
using System.Windows.Media;
using RouterPlus.App;

namespace RouterPlus.App.Tests;

public sealed class ThemeManagerTests
{
    [Fact]
    public void ApplyTo_UsesMergedDictionaryContainingThemeKey()
    {
        // Arrange
        var resources = CreateResourcesWithThemeDictionary();
        var themeDictionary = resources.MergedDictionaries[0];
        var unrelatedDictionary = new ResourceDictionary
        {
            ["UnrelatedBrush"] = new SolidColorBrush(Colors.Magenta)
        };
        resources.MergedDictionaries.Insert(0, unrelatedDictionary);

        // Act
        ThemeManager.ApplyTo(resources, useLightTheme: true);

        // Assert
        Assert.Equal("#FFF5F7FB", ((SolidColorBrush)themeDictionary["SurfaceBrush"]).Color.ToString());
        Assert.Equal(Colors.Magenta, ((SolidColorBrush)unrelatedDictionary["UnrelatedBrush"]).Color);
    }

    [Fact]
    public void ApplyTo_UsesRootResources_WhenNoMergedDictionaryContainsThemeKey()
    {
        // Arrange
        var resources = new ResourceDictionary
        {
            ["SurfaceBrush"] = new SolidColorBrush(Colors.Magenta),
            ["TextBrush"] = new SolidColorBrush(Colors.Magenta)
        };
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            ["UnrelatedBrush"] = new SolidColorBrush(Colors.Magenta)
        });

        // Act
        ThemeManager.ApplyTo(resources, useLightTheme: false);

        // Assert
        Assert.Equal("#FF0A0F1A", ((SolidColorBrush)resources["SurfaceBrush"]).Color.ToString());
        Assert.Equal("#FFEDF4FF", ((SolidColorBrush)resources["TextBrush"]).Color.ToString());
    }

    [Fact]
    public void ApplyTo_SkipsThemeKeysWithNonBrushValues()
    {
        // Arrange
        var resources = new ResourceDictionary
        {
            ["SurfaceBrush"] = "not a brush",
            ["TextBrush"] = new SolidColorBrush(Colors.Magenta)
        };

        // Act
        ThemeManager.ApplyTo(resources, useLightTheme: true);

        // Assert
        Assert.Equal("not a brush", resources["SurfaceBrush"]);
        Assert.Equal("#FF172033", ((SolidColorBrush)resources["TextBrush"]).Color.ToString());
    }

    private static ResourceDictionary CreateResourcesWithThemeDictionary()
    {
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            ["SurfaceBrush"] = new SolidColorBrush(Colors.Magenta),
            ["TextBrush"] = new SolidColorBrush(Colors.Magenta)
        });
        return resources;
    }
}
