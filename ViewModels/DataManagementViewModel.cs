using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>
/// v0.6.0：设置页“数据管理”区 ViewModel：
/// 导出便携备份、导入便携备份、打开本地数据文件夹、备份内容说明。
/// 备份不含 API Key；导入为安全合并（保留本机凭据，新账户需重新输入密钥）。
/// </summary>
public sealed partial class DataManagementViewModel : ObservableObject
{
    private readonly IPortableBackupService _backup;
    private readonly IFilePickerService _filePicker;
    private readonly ILocalDataFolderOpener _dataFolderOpener;
    private readonly AppLog? _log;
    private readonly CancellationTokenSource _lifetime = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    public string DescriptionText =>
        "便携备份包含账户元数据、Provider 非敏感设置、余额历史、阈值与设置；不包含 API Key 或凭据。";

    public IAsyncRelayCommand ExportBackupCommand { get; }

    public IAsyncRelayCommand ImportBackupCommand { get; }

    public IAsyncRelayCommand OpenDataFolderCommand { get; }

    public DataManagementViewModel(
        IPortableBackupService backup,
        IFilePickerService filePicker,
        ILocalDataFolderOpener dataFolderOpener,
        AppLog? log = null)
    {
        _backup = backup;
        _filePicker = filePicker;
        _dataFolderOpener = dataFolderOpener;
        _log = log ?? new AppLog(System.IO.Path.GetTempPath());

        ExportBackupCommand = new AsyncRelayCommand(ExportBackupAsync, () => !IsBusy);
        ImportBackupCommand = new AsyncRelayCommand(ImportBackupAsync, () => !IsBusy);
        OpenDataFolderCommand = new AsyncRelayCommand(OpenDataFolderAsync, () => !IsBusy);
    }

    partial void OnIsBusyChanged(bool value)
    {
        ExportBackupCommand.NotifyCanExecuteChanged();
        ImportBackupCommand.NotifyCanExecuteChanged();
        OpenDataFolderCommand.NotifyCanExecuteChanged();
    }

    private async Task ExportBackupAsync()
    {
        string? path = await _filePicker.PickSaveFileAsync(
            $"ApiMonitor-backup-{DateTimeOffset.Now:yyyyMMdd}.{PortableBackupConstants.Extension}",
            new[] { $".{PortableBackupConstants.Extension}" },
            _lifetime.Token);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        IsBusy = true;
        HasStatus = false;
        try
        {
            await _backup.ExportAsync(path, _lifetime.Token);
            StatusText = "便携备份已导出（不包含 API Key）。";
            HasStatus = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"导出备份失败: {ex.GetType().Name}");
            StatusText = "导出备份失败，请稍后重试。";
            HasStatus = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportBackupAsync()
    {
        string? path = await _filePicker.PickOpenFileAsync(
            new[] { $".{PortableBackupConstants.Extension}" },
            _lifetime.Token);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        IsBusy = true;
        HasStatus = false;
        try
        {
            var preview = await _backup.InspectAsync(path, _lifetime.Token);
            string providerSummary = preview.ProviderIds.Count == 0
                ? "无 Provider 数据"
                : string.Join("、", preview.ProviderIds);
            StatusText =
                $"备份可导入：{preview.AccountCount} 个账户、{preview.HistoryEntryCount} 条历史；" +
                $"Provider：{providerSummary}。导入为安全合并，不包含 API Key。";
            HasStatus = true;

            // 本版本只实现安全合并：保留本机已有账户与凭据，新账户标记“需要重新输入凭据”。
            var result = await _backup.ImportAsync(path, BackupMergePreference.KeepLocal, _lifetime.Token);
            string needing = result.AccountsNeedingCredential.Count > 0
                ? $" 需要重新输入 API Key：{string.Join("、", result.AccountsNeedingCredential)}"
                : string.Empty;
            StatusText =
                $"导入完成：新增 {result.AddedAccounts}、更新 {result.UpdatedAccounts}、" +
                $"跳过 {result.SkippedAccounts}、失败 {result.FailedAccounts}；" +
                $"历史新增 {result.AddedHistoryEntries} 条、跳过 {result.SkippedHistoryEntries} 条。{needing}";
            HasStatus = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"导入备份失败: {ex.GetType().Name}");
            StatusText = $"导入失败：{ex.Message}（已回滚本次变更）。";
            HasStatus = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenDataFolderAsync()
    {
        IsBusy = true;
        try
        {
            bool ok = await _dataFolderOpener.OpenAsync();
            if (!ok)
            {
                StatusText = "无法打开本地数据文件夹。";
                HasStatus = true;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Shutdown() => _lifetime.Cancel();
}
