using RouterPlus.Core.Updates;

namespace RouterPlus.Infrastructure.Updates;

public static class UpdatePaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "9RouterPlus",
        "updates");

    public static string VersionRoot(ReleaseVersion version) =>
        ResolveUnderRoot(Root, version.ToString());

    public static string ResolveUnderRoot(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var canonicalRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
        if (!candidate.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Update path escapes its allowed root.");
        }

        EnsureNoReparsePoints(canonicalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), candidate);
        return candidate;
    }

    private static void EnsureNoReparsePoints(string root, string candidate)
    {
        var current = root;
        if (PathExists(current) && IsReparsePoint(current))
        {
            throw new InvalidDataException("Update path uses a reparse point.");
        }

        var relativePath = Path.GetRelativePath(root, candidate);
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!PathExists(current))
            {
                break;
            }

            if (IsReparsePoint(current))
            {
                throw new InvalidDataException("Update path uses a reparse point.");
            }
        }
    }

    private static bool PathExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
