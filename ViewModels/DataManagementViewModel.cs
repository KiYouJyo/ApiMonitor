using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>本地便携备份 + WebDAV 传输层。</summary>
public sealed partial class DataManagementViewModel : ObservableObject
{
    private readonly IPortableBackupService _backup;
    private readonly IFilePickerService _filePicker;
    private readonly ILocalDataFolderOpener _dataFolderOpener;
    private readonly WebDavBackupService _webDav;
    private readonly AppLog _log;
    private readonly CancellationTokenSource _lifetime = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private bool _isWebDavBusy;
    [ObservableProperty] private string _webDavEndpoint = string.Empty;
    [ObservableProperty] private string _webDavUserName = string.Empty;
    [ObservableProperty] private string _webDavPassword = string.Empty;
    [ObservableProperty] private string _webDavRemoteDirectory = "ApiMonitor/Backups";
    [ObservableProperty] private bool _webDavAutoBackupEnabled;
    [ObservableProperty] private string _webDavStatusText = string.Empty;
    [ObservableProperty] private bool _hasWebDavStatus;
    [ObservableProperty] private string _webDavLastBackupText = string.Empty;

    public string DescriptionText => L10n.Get("Data.BackupDescription");
    public IAsyncRelayCommand ExportBackupCommand { get; }
    public IAsyncRelayCommand ImportBackupCommand { get; }
    public IAsyncRelayCommand OpenDataFolderCommand { get; }

    public DataManagementViewModel(
        IPortableBackupService backup,
        IFilePickerService filePicker,
        ILocalDataFolderOpener dataFolderOpener,
        AppLog? log = null,
        WebDavBackupService? webDav = null)
    {
        SupplementalResourceBootstrap.EnsureInitialized();
        _backup = backup;
        _filePicker = filePicker;
        _dataFolderOpener = dataFolderOpener;
        _log = log ?? new AppLog(Path.GetTempPath());
        _webDav = webDav ?? new WebDavBackupService(
            backup,
            new CredentialLockerSecretStore(_log),
            AppPaths.GetLocalDataDirectory());

        ExportBackupCommand = new AsyncRelayCommand(ExportBackupAsync, () => !IsBusy);
        ImportBackupCommand = new AsyncRelayCommand(ImportBackupAsync, () => !IsBusy);
        OpenDataFolderCommand = new AsyncRelayCommand(OpenDataFolderAsync, () => !IsBusy);

        LoadWebDavSettings();
        _ = TryAutomaticWebDavBackupAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        ExportBackupCommand.NotifyCanExecuteChanged();
        ImportBackupCommand.NotifyCanExecuteChanged();
        OpenDataFolderCommand.NotifyCanExecuteChanged();
    }

    private void LoadWebDavSettings()
    {
        var settings = _webDav.LoadSettings();
        WebDavEndpoint = settings.Endpoint;
        WebDavUserName = settings.UserName;
        WebDavRemoteDirectory = settings.RemoteDirectory;
        WebDavAutoBackupEnabled = settings.AutoBackupEnabled;
        UpdateLastBackupText(settings.LastBackupUtc);
    }

    private WebDavBackupSettings CurrentWebDavSettings() => new()
    {
        Endpoint = WebDavEndpoint.Trim(),
        UserName = WebDavUserName.Trim(),
        RemoteDirectory = WebDavRemoteDirectory.Trim(),
        AutoBackupEnabled = WebDavAutoBackupEnabled,
        LastBackupUtc = _webDav.LoadSettings().LastBackupUtc,
    };

    [RelayCommand]
    private async Task SaveWebDavSettingsAsync()
    {
        if (IsWebDavBusy) return;
        IsWebDavBusy = true;
        try
        {
            await PersistWebDavSettingsAsync(_lifetime.Token);
            ShowWebDavStatus(L10n.Get("WebDav.StatusSaved"));
        }
        catch (Exception ex)
        {
            ShowWebDavError(ex);
        }
        finally { IsWebDavBusy = false; }
    }

    [RelayCommand]
    private async Task TestWebDavAsync()
    {
        if (IsWebDavBusy) return;
        IsWebDavBusy = true;
        ShowWebDavStatus(L10n.Get("WebDav.StatusTesting"));
        try
        {
            var settings = await PersistWebDavSettingsAsync(_lifetime.Token);
            await _webDav.TestConnectionAsync(settings, _lifetime.Token);
            ShowWebDavStatus(L10n.Get("WebDav.StatusConnected"));
        }
        catch (Exception ex)
        {
            ShowWebDavError(ex);
        }
        finally { IsWebDavBusy = false; }
    }

    [RelayCommand]
    private async Task BackupToWebDavAsync()
    {
        if (IsWebDavBusy) return;
        IsWebDavBusy = true;
        ShowWebDavStatus(L10n.Get("WebDav.StatusBackingUp"));
        try
        {
            var settings = await PersistWebDavSettingsAsync(_lifetime.Token);
            var entry = await _webDav.UploadAsync(settings, _lifetime.Token);
            UpdateLastBackupText(settings.LastBackupUtc);
            ShowWebDavStatus(L10n.Format("WebDav.StatusBackupUploaded", entry.Name));
        }
        catch (Exception ex)
        {
            ShowWebDavError(ex);
        }
        finally { IsWebDavBusy = false; }
    }

    [RelayCommand]
    private async Task RestoreLatestWebDavAsync()
    {
        if (IsWebDavBusy) return;
        IsWebDavBusy = true;
        ShowWebDavStatus(L10n.Get("WebDav.StatusRestoreStarted"));
        try
        {
            var settings = await PersistWebDavSettingsAsync(_lifetime.Token);
            var result = await _webDav.RestoreLatestAsync(settings, _lifetime.Token);
            ShowWebDavStatus(result is null
                ? L10n.Get("WebDav.StatusNoRemoteBackup")
                : L10n.Format("WebDav.StatusRestoreFinished", result.AddedAccounts, result.UpdatedAccounts));
        }
        catch (Exception ex)
        {
            ShowWebDavError(ex);
        }
        finally { IsWebDavBusy = false; }
    }

    private async Task<WebDavBackupSettings> PersistWebDavSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = CurrentWebDavSettings();
        if (!string.IsNullOrEmpty(WebDavPassword))
        {
            await _webDav.SetPasswordAsync(WebDavPassword, cancellationToken);
            WebDavPassword = string.Empty;
        }
        _webDav.SaveSettings(settings);
        return settings;
    }

    private async Task TryAutomaticWebDavBackupAsync()
    {
        try
        {
            var settings = _webDav.LoadSettings();
            if (!settings.AutoBackupEnabled || string.IsNullOrWhiteSpace(settings.Endpoint)) return;
            if (settings.LastBackupUtc is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(24)) return;
            await _webDav.UploadAsync(settings, _lifetime.Token);
            UpdateLastBackupText(settings.LastBackupUtc);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Error($"WebDAV 自动备份失败: {ex.GetType().Name}");
        }
    }

    private void ShowWebDavError(Exception ex)
    {
        _log.Error($"WebDAV 操作失败: {ex.GetType().Name}");
        string text = ex is InvalidOperationException { Message: "WebDavInvalidEndpoint" }
            ? L10n.Get("WebDav.InvalidEndpoint")
            : ex is InvalidOperationException { Message: "WebDavPasswordRequired" }
                ? L10n.Get("WebDav.PasswordRequired")
                : L10n.Format("WebDav.StatusFailed", ex.GetType().Name);
        ShowWebDavStatus(text);
    }

    private void ShowWebDavStatus(string text)
    {
        WebDavStatusText = text;
        HasWebDavStatus = true;
    }

    private void UpdateLastBackupText(DateTimeOffset? utc)
    {
        WebDavLastBackupText = utc is null
            ? string.Empty
            : L10n.Format("Settings.WebDavLastBackupFormat", utc.Value.ToLocalTime().ToString("g"));
    }

    private async Task ExportBackupAsync()
    {
        string? path = await _filePicker.PickSaveFileAsync(
            $"ApiMonitor-backup-{DateTimeOffset.Now:yyyyMMdd}.{PortableBackupConstants.Extension}",
            new[] { $".{PortableBackupConstants.Extension}" },
            _lifetime.Token);
        if (string.IsNullOrEmpty(path)) return;

        IsBusy = true;
        HasStatus = false;
        try
        {
            await _backup.ExportAsync(path, _lifetime.Token);
            StatusText = L10n.Get("About.BackupExported");
            HasStatus = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Error($"导出备份失败: {ex.GetType().Name}");
            StatusText = L10n.Get("About.BackupExportFailed");
            HasStatus = true;
        }
        finally { IsBusy = false; }
    }

    private async Task ImportBackupAsync()
    {
        string? path = await _filePicker.PickOpenFileAsync(new[] { $".{PortableBackupConstants.Extension}" }, _lifetime.Token);
        if (string.IsNullOrEmpty(path)) return;

        IsBusy = true;
        HasStatus = false;
        try
        {
            var preview = await _backup.InspectAsync(path, _lifetime.Token);
            string providerSummary = preview.ProviderIds.Count == 0
                ? L10n.Get("Data.NoProviderData")
                : string.Join("、", preview.ProviderIds);
            StatusText = L10n.Format("Data.ImportPreview", preview.AccountCount, preview.HistoryEntryCount)
                + L10n.Format("Data.ImportPreviewProvider", providerSummary);
            HasStatus = true;

            var result = await _backup.ImportAsync(path, BackupMergePreference.KeepLocal, _lifetime.Token);
            string needing = result.AccountsNeedingCredential.Count > 0
                ? L10n.Format("About.BackupImportNeedingKey", string.Join("、", result.AccountsNeedingCredential))
                : string.Empty;
            StatusText = L10n.Format("Data.ImportDone", result.AddedAccounts, result.UpdatedAccounts)
                + L10n.Format("Data.ImportSkipped", result.SkippedAccounts, result.FailedAccounts)
                + L10n.Format("Data.ImportHistory", result.AddedHistoryEntries, result.SkippedHistoryEntries, needing);
            HasStatus = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Error($"导入备份失败: {ex.GetType().Name}");
            StatusText = L10n.Format("About.BackupImportFailed", ex.Message);
            HasStatus = true;
        }
        finally { IsBusy = false; }
    }

    private async Task OpenDataFolderAsync()
    {
        IsBusy = true;
        try
        {
            bool ok = await _dataFolderOpener.OpenAsync();
            if (!ok)
            {
                StatusText = L10n.Get("About.OpenDataFolderFailed");
                HasStatus = true;
            }
        }
        finally { IsBusy = false; }
    }

    public void Shutdown()
    {
        _lifetime.Cancel();
        _webDav.Dispose();
    }
}
