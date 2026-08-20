using System.Windows;
using System.Windows.Media;

namespace RouterPlus.App;

public static class ThemeManager
{
    public static void Apply(bool useLightTheme)
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var themeResources = resources.MergedDictionaries
            .FirstOrDefault(dictionary => dictionary.Contains("SurfaceBrush"))
            ?? resources;

        var palette = useLightTheme
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SurfaceBrush"] = "#F5F7FB",
                ["SidebarBrush"] = "#E8EDF5",
                ["PanelBrush"] = "#FFFFFF",
                ["PanelLightBrush"] = "#EFF4FB",
                ["TextBrush"] = "#172033",
                ["AvatarTextBrush"] = "#FFFFFF",
                ["MutedTextBrush"] = "#61708A",
                ["AccentBrush"] = "#0F766E",
                ["AccentDarkBrush"] = "#115E59",
                ["AccentSoftBrush"] = "#D8F3EE",
                ["InfoSoftBrush"] = "#E4ECFA",
                ["BorderBrush"] = "#CBD5E1",
                ["SuccessBrush"] = "#0F9F6E",
                ["WarningBrush"] = "#B7791F",
                ["DangerBrush"] = "#D53F5F",
                ["AccentContentBrush"] = "#FFFFFF"
            }
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SurfaceBrush"] = "#0A0F1A",
                ["SidebarBrush"] = "#0D1624",
                ["PanelBrush"] = "#141F31",
                ["PanelLightBrush"] = "#1B2B43",
                ["TextBrush"] = "#EDF4FF",
                ["AvatarTextBrush"] = "#FFFFFF",
                ["MutedTextBrush"] = "#8FA1BB",
                ["AccentBrush"] = "#6CE0C6",
                ["AccentDarkBrush"] = "#276477",
                ["AccentSoftBrush"] = "#263F4A",
                ["InfoSoftBrush"] = "#253452",
                ["BorderBrush"] = "#293B54",
                ["SuccessBrush"] = "#70E0A9",
                ["WarningBrush"] = "#F7C873",
                ["DangerBrush"] = "#FF8D9E",
                ["AccentContentBrush"] = "#0A0F1A"
            };

        foreach (var entry in palette)
        {
            if (themeResources[entry.Key] is not SolidColorBrush)
            {
                continue;
            }

            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(entry.Value)!;
            themeResources[entry.Key] = new SolidColorBrush(color);
        }
    }
}
