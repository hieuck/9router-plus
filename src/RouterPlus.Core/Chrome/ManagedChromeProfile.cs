namespace RouterPlus.Core.Chrome;

public sealed record ManagedChromeProfile(
    string Name,
    string DirectoryName,
    string UserDataDirectory);
