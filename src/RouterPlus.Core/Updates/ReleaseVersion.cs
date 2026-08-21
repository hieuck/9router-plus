using System.Globalization;
using System.Text.RegularExpressions;

namespace RouterPlus.Core.Updates;

public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private static readonly Regex Format = new(
        "^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly string[] _prereleaseIdentifiers;

    private ReleaseVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        _prereleaseIdentifiers = string.IsNullOrWhiteSpace(prerelease)
            ? Array.Empty<string>()
            : prerelease.Split('.', StringSplitOptions.None);
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? Prerelease { get; }

    public bool IsPrerelease => _prereleaseIdentifiers.Length > 0;

    public static ReleaseVersion Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('+', StringComparison.Ordinal))
        {
            throw new FormatException("Release versions must not contain build metadata.");
        }

        var match = Format.Match(value);
        if (!match.Success)
        {
            throw new FormatException($"Invalid release version: {value}");
        }

        var prerelease = match.Groups[4].Success ? match.Groups[4].Value : null;
        if (prerelease is not null)
        {
            foreach (var identifier in prerelease.Split('.', StringSplitOptions.None))
            {
                if (identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsDigit))
                {
                    throw new FormatException($"Invalid prerelease identifier: {identifier}");
                }
            }
        }

        return new ReleaseVersion(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
            prerelease);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        if (!IsPrerelease && !other.IsPrerelease)
        {
            return 0;
        }

        if (!IsPrerelease)
        {
            return 1;
        }

        if (!other.IsPrerelease)
        {
            return -1;
        }

        for (var index = 0; index < Math.Min(_prereleaseIdentifiers.Length, other._prereleaseIdentifiers.Length); index++)
        {
            var left = _prereleaseIdentifiers[index];
            var right = other._prereleaseIdentifiers[index];
            var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
            if (leftNumeric && rightNumeric)
            {
                result = leftNumber.CompareTo(rightNumber);
            }
            else if (leftNumeric != rightNumeric)
            {
                result = leftNumeric ? -1 : 1;
            }
            else
            {
                result = string.CompareOrdinal(left, right);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return _prereleaseIdentifiers.Length.CompareTo(other._prereleaseIdentifiers.Length);
    }

    public bool Equals(ReleaseVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public override string ToString() => IsPrerelease
        ? $"{Major}.{Minor}.{Patch}-{Prerelease}"
        : $"{Major}.{Minor}.{Patch}";
}
