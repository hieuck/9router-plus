namespace RouterPlus.Infrastructure.Storage;

public sealed record RecentProfile(
    string ProfileId,
    string ProfileName,
    string UserDataDirectory,
    DateTime LastUsedUtc,
    int LaunchCount = 1,
    bool IsPinned = false);
