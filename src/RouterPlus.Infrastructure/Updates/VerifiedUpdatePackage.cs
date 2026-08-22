using RouterPlus.Core.Updates;

namespace RouterPlus.Infrastructure.Updates;

public sealed record VerifiedUpdatePackage(
    ReleaseVersion Version,
    string ArchivePath,
    string StagingPath);
