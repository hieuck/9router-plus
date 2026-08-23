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
            var assemblyVersion = assembly.GetName().Version ?? new Version(0, 0, 0);
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return ParseVersion(informationalVersion, assemblyVersion);
        }
    }

    public static ReleaseVersion ParseVersion(string? informationalVersion, Version assemblyVersion)
    {
        var candidate = informationalVersion?.Trim();
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            candidate = candidate.StartsWith('v') ? candidate[1..] : candidate;
            try
            {
                return ReleaseVersion.Parse(candidate);
            }
            catch (FormatException)
            {
                // CI builds may use a commit SHA as informational version.
            }
        }

        return ReleaseVersion.Parse(
            $"{Math.Max(0, assemblyVersion.Major)}.{Math.Max(0, assemblyVersion.Minor)}.{Math.Max(0, assemblyVersion.Build)}");
    }
}
