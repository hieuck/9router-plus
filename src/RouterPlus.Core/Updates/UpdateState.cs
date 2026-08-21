namespace RouterPlus.Core.Updates;

public enum UpdateState
{
    Idle,
    Checking,
    Available,
    Downloading,
    Verifying,
    ReadyToInstall,
    Installing,
    Completed,
    Failed,
    Disabled
}
