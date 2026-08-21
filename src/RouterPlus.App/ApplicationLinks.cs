using System.IO;
using System.Diagnostics;

namespace RouterPlus.App;

public static class ApplicationLinks
{
    public static readonly Uri RepositoryUri = new("https://github.com/hieuck/9router-plus");
    public static readonly Uri HelpUri = new("https://github.com/hieuck/9router-plus/blob/master/docs/user-guide.md");
    public static readonly Uri SecurityUri = new("https://github.com/hieuck/9router-plus/blob/master/SECURITY.md");
    public static readonly Uri ReleaseUri = new("https://github.com/hieuck/9router-plus/releases/latest");

    public static bool IsAllowed(Uri uri) =>
        uri is not null
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && (string.Equals(uri.AbsolutePath, "/hieuck/9router-plus", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.StartsWith("/hieuck/9router-plus/", StringComparison.OrdinalIgnoreCase));
}

public interface IExternalLinkLauncher
{
    void Open(Uri uri);
}

public sealed class ShellLinkLauncher : IExternalLinkLauncher
{
    public void Open(Uri uri)
    {
        if (!ApplicationLinks.IsAllowed(uri))
        {
            throw new InvalidDataException("External link is not allowlisted.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }
}
