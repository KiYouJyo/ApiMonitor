using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

public sealed partial class AboutViewModel
{
    [ObservableProperty]
    private double _updateInstallProgress;

    [ObservableProperty]
    private bool _hasUpdateInstallProgress;

    [RelayCommand]
    private async Task InstallAvailableUpdateAsync()
    {
        if (IsCheckingUpdates) return;
        IsCheckingUpdates = true;
        HasUpdateInstallProgress = false;
        UpdateStatusText = L10n.Get("Update.Installing");
        HasUpdateStatus = true;

        try
        {
            UpdateCheckResult result;
            if (_updateService is GitHubUpdateService github)
            {
                EventHandler<double> handler = (_, progress) =>
                {
                    UpdateInstallProgress = Math.Clamp(progress * 100d, 0d, 100d);
                    HasUpdateInstallProgress = true;
                };
                github.DownloadProgressChanged += handler;
                try
                {
                    result = await github.RequestInstallAsync(_lifetime.Token);
                }
                finally
                {
                    github.DownloadProgressChanged -= handler;
                }
            }
            else if (_updateService is MicrosoftStoreUpdateService store)
            {
                result = await store.RequestInstallAsync(_lifetime.Token);
            }
            else
            {
                result = new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Failed,
                    ErrorMessage = L10n.Get("Update.InstallUnsupported"),
                };
            }

            UpdateStatusText = result.Status == UpdateCheckStatus.UpToDate
                ? L10n.Get("Update.InstallQueued")
                : L10n.Format("About.UpdateCheckFailedFormat", result.ErrorMessage ?? L10n.Get("Common.Unknown"));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"应用内安装更新失败: {ex.GetType().Name}");
            UpdateStatusText = L10n.Get("Update.InstallFailed");
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }
}
