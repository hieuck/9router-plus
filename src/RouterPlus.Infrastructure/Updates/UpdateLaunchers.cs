using System.Diagnostics;
using RouterPlus.Core.Updates;

namespace RouterPlus.Infrastructure.Updates;

public interface IUpdaterProcessLauncher
{
    Task<bool> LaunchAsync(
        string updaterPath,
        string targetDirectory,
        string stagingDirectory,
        string backupDirectory,
        int processId,
        ReleaseVersion version,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsUpdaterProcessLauncher : IUpdaterProcessLauncher
{
    public Task<bool> LaunchAsync(
        string updaterPath,
        string targetDirectory,
        string stagingDirectory,
        string backupDirectory,
        int processId,
        ReleaseVersion version,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(updaterPath))
        {
            return Task.FromResult(false);
        }

        var helperDirectory = Path.Combine(
            Path.GetTempPath(),
            "9RouterPlus",
            "updater",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(helperDirectory);
        var helperPath = Path.Combine(helperDirectory, "RouterPlus.Updater.exe");
        File.Copy(updaterPath, helperPath, overwrite: false);

        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            WorkingDirectory = targetDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--pid");
        startInfo.ArgumentList.Add(processId.ToString());
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(targetDirectory);
        startInfo.ArgumentList.Add("--staging");
        startInfo.ArgumentList.Add(stagingDirectory);
        startInfo.ArgumentList.Add("--backup");
        startInfo.ArgumentList.Add(backupDirectory);
        startInfo.ArgumentList.Add("--app");
        startInfo.ArgumentList.Add(Path.Combine(targetDirectory, "RouterPlus.exe"));
        startInfo.ArgumentList.Add("--version");
        startInfo.ArgumentList.Add(version.ToString());
        startInfo.ArgumentList.Add("--restart");
        return Task.FromResult(Process.Start(startInfo) is not null);
    }

}
