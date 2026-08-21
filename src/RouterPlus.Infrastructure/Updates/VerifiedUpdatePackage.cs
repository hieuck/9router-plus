using RouterPlus.Core.Updates;

namespace RouterPlus.Infrastructure.Updates;

public sealed record VerifiedUpdatePackage(
    ReleaseManifest Manifest,
    string ArchivePath,
    string StagingPath);
