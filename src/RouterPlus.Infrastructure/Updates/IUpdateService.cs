using RouterPlus.Core.Updates;

namespace RouterPlus.Infrastructure.Updates;

public interface IUpdateService
{
    bool IsInstallSupported { get; }

    Task<ReleaseCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    Task<VerifiedUpdatePackage> DownloadAndStageAsync(
        ReleaseCheckResult release,
        CancellationToken cancellationToken = default);

    Task<bool> LaunchUpdaterAsync(
        VerifiedUpdatePackage package,
        CancellationToken cancellationToken = default);
}
