using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Core.Updates;
using RouterPlus.App;
using RouterPlus.App.Views;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Services;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Infrastructure.Updates;
using RouterPlus.App.Services;

namespace RouterPlus.App.ViewModels;

public sealed partial class MainViewModel
{
    public async Task CheckForUpdatesAsync()
    {
        if (IsUpdateChecking)
        {
            return;
        }

        IsUpdateChecking = true;
        UpdateState = UpdateState.Checking;
        UpdateStatusText = "Đang kiểm tra bản cập nhật…";
        try
        {
            var release = await _updateService.CheckAsync();
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Updates",
                "UpdateCheckCompleted",
                "Update check completed",
                new { is_update_available = release.IsUpdateAvailable, available_version = release.AvailableVersion });
            _latestRelease = release;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(AvailableVersion));

            if (!_updateService.IsInstallSupported)
            {
                UpdateState = UpdateState.Disabled;
                UpdateStatusText = release.IsUpdateAvailable
                    ? $"Có bản {release.AvailableVersion}, nhưng tự cập nhật chỉ hỗ trợ trên Windows."
                    : "Tự cập nhật chỉ hỗ trợ trên Windows.";
            }
            else if (release.IsUpdateAvailable)
            {
                UpdateState = UpdateState.Available;
                UpdateStatusText = $"Có bản cập nhật {release.AvailableVersion}.";
            }
            else
            {
                UpdateState = UpdateState.Idle;
                UpdateStatusText = "Bạn đang dùng bản mới nhất.";
            }
        }
        catch (OperationCanceledException)
        {
            UpdateState = UpdateState.Failed;
            UpdateStatusText = "Đã hủy kiểm tra bản cập nhật.";
        }
        catch
        {
            _latestRelease = null;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(AvailableVersion));
            UpdateState = _updateService.IsInstallSupported ? UpdateState.Failed : UpdateState.Disabled;
            UpdateStatusText = _updateService.IsInstallSupported
                ? "Không thể kiểm tra bản cập nhật lúc này."
                : "Không thể bật tự cập nhật trên hệ điều hành này.";
        }
        finally
        {
            IsUpdateChecking = false;
        }
    }

    public async Task<bool> InstallUpdateAsync(bool confirmedByUser)
    {
        if (!confirmedByUser || !CanInstallUpdate || _latestRelease is null)
        {
            return false;
        }

        _isUpdateInstalling = true;
        UpdateState = UpdateState.Downloading;
        UpdateStatusText = "Đang tải và kiểm tra bản cập nhật…";
        InstallUpdateCommand.RaiseCanExecuteChanged();
        try
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Updates",
                "DownloadingUpdate",
                "Downloading and staging update package",
                new { version = _latestRelease.AvailableVersion });
            var package = await _updateService.DownloadAndStageAsync(_latestRelease);
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Updates",
                "UpdateStaged",
                "Update package staged; launching updater",
                new { archive_path = package.ArchivePath, staging_path = package.StagingPath });
            UpdateState = UpdateState.Installing;
            UpdateStatusText = "Bản cập nhật đã được xác minh. Đang chuẩn bị khởi động lại…";
            if (!await _updateService.LaunchUpdaterAsync(package))
            {
                UpdateState = UpdateState.Failed;
                UpdateStatusText = "Không thể khởi động trình cập nhật. Bản đang chạy không bị thay đổi.";
                return false;
            }

            UpdateState = UpdateState.Completed;
            UpdateStatusText = "Đã chuẩn bị cập nhật. Ứng dụng sẽ đóng để hoàn tất thay thế an toàn.";
            return true;
        }
        catch (OperationCanceledException)
        {
            UpdateState = UpdateState.Failed;
            UpdateStatusText = "Đã hủy cập nhật. Bản đang chạy không bị thay đổi.";
            return false;
        }
        catch
        {
            UpdateState = UpdateState.Failed;
            UpdateStatusText = "Không thể xác minh hoặc cài đặt bản cập nhật. Bản đang chạy không bị thay đổi.";
            return false;
        }
        finally
        {
            _isUpdateInstalling = false;
            InstallUpdateCommand.RaiseCanExecuteChanged();
        }
    }

}