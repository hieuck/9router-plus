namespace RouterPlus.Core.Updates;

public sealed record ReleaseAsset(
    string Name,
    Uri DownloadUri,
    long Length,
    string? Sha256,
    bool IsRequired);
