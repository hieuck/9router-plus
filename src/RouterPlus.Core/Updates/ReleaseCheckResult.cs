namespace RouterPlus.Core.Updates;

public sealed record ReleaseCheckResult(
    ReleaseVersion CurrentVersion,
    ReleaseVersion? AvailableVersion,
    string? ReleaseNotes,
    ReleaseAsset? Archive,
    ReleaseAsset? Checksum)
{
    public bool IsUpdateAvailable => AvailableVersion is not null;
}
