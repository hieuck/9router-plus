using RouterPlus.App;
using System.Reflection;
using RouterPlus.Core.Updates;

namespace RouterPlus.App.ViewModels;

public sealed class AboutViewModel
{
    public AboutViewModel()
    {
        ProductName = "9Router Profile Tool";
        Version = ApplicationInfo.CurrentVersion.ToString();
        LicenseName = "MIT License";
        RepositoryUri = ApplicationLinks.RepositoryUri;
        HelpUri = ApplicationLinks.HelpUri;
        SecurityUri = ApplicationLinks.SecurityUri;
        ReleaseUri = ApplicationLinks.ReleaseUri;
    }

    public string ProductName { get; }

    public string Version { get; }

    public string LicenseName { get; }

    public Uri RepositoryUri { get; }

    public Uri HelpUri { get; }

    public Uri SecurityUri { get; }

    public Uri ReleaseUri { get; }
}

public static class ApplicationInfo
{
    public const string Publisher = "9Router Project";

    public static ReleaseVersion CurrentVersion
    {
        get
        {
            var assembly = typeof(ApplicationInfo).Assembly;
            var version = assembly.GetName().Version;
            if (version is null)
            {
                return ReleaseVersion.Parse("0.0.0");
            }

            return ReleaseVersion.Parse($"{Math.Max(0, version.Major)}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}");
        }
    }
}
