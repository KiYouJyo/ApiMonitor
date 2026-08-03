using System.Collections.ObjectModel;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>关于页 Provider 能力行。</summary>
public sealed record AboutProviderRow(string ProviderId, string DisplayName);

/// <summary>关于页链接项。</summary>
public sealed record AboutLinkRow(string Key, string Title, string Url);

/// <summary>
/// v0.6.0：完整“关于”页 ViewModel。
/// 版本来自统一元数据服务 AppInfo（不在 XAML 硬编码）；
/// Provider 列表来自 Provider 注册表（不写死）；
/// 更新检查只在用户点击时访问 GitHub，不自动下载/安装。
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IUpdateCheckService _updateCheck;
    private readonly IDiagnosticsInfoService _diagnostics;
    private readonly IClipboardService _clipboard;
    private readonly IExternalLinkLauncher _launcher;
    private readonly ILocalDataFolderOpener _dataFolderOpener;
    private readonly IFilePickerService _filePicker;
    private readonly IPortableBackupService _backup;
    private readonly AppLog? _log;
    private readonly CancellationTokenSource _lifetime = new();

    // ---- 产品信息（来自统一元数据服务） ----
    public string ProductName => "ApiMonitor";

    public string Tagline => L10n.Get("About.TaglineText");

    public string DisplayVersionText => L10n.Format("About.DisplayVersionFormat", AppInfo.DisplayVersion);

    public string PackageVersionText => L10n.Format("About.PackageVersionFormat", AppInfo.PackageVersion);

    public string ArchitectureText => L10n.Format("About.ArchitectureFormat", AppInfo.Architecture);

    public string PackageIdentityText => L10n.Format("About.PackageIdentityFormat", AppInfo.PackageIdentity);

    public string PublisherText => L10n.Format("About.PublisherFormat", AppInfo.Publisher);

    public string LicenseText => "MIT License";

    public string CopyrightText => "Copyright (c) 2026 KiYouJyo";

    // ---- 当前能力（动态，来自注册表） ----
    public ObservableCollection<AboutProviderRow> Providers { get; } = new();

    public string ProviderCountText => L10n.Format("About.ProviderCountFormat", Providers.Count);

    // ---- 隐私与安全摘要 ----
    public IReadOnlyList<string> PrivacyItems { get; } = new[]
    {
        L10n.Get("About.PrivacyItemCredentialLocker"),
        L10n.Get("About.PrivacyItemLocalData"),
        L10n.Get("About.PrivacyItemNoCloud"),
        L10n.Get("About.PrivacyItemNoTelemetry"),
        L10n.Get("About.PrivacyItemLocalNotifications"),
        L10n.Get("About.PrivacyItemBackupNoSecrets"),
    };

    // ---- 项目链接 ----
    public IReadOnlyList<AboutLinkRow> Links { get; } = new[]
    {
        new AboutLinkRow("Repository", L10n.Get("About.LinkRepositoryTitle"), "https://github.com/KiYouJyo/ApiMonitor"),
        new AboutLinkRow("Releases", L10n.Get("About.LinkReleasesTitle"), "https://github.com/KiYouJyo/ApiMonitor/releases"),
        new AboutLinkRow("Issues", L10n.Get("About.LinkIssuesTitle"), "https://github.com/KiYouJyo/ApiMonitor/issues"),
        new AboutLinkRow("Privacy", L10n.Get("About.LinkPrivacyTitle"), "https://github.com/KiYouJyo/ApiMonitor/blob/main/PRIVACY.md"),
        new AboutLinkRow("Security", L10n.Get("About.LinkSecurityTitle"), "https://github.com/KiYouJyo/ApiMonitor/blob/main/SECURITY.md"),
        new AboutLinkRow("Support", L10n.Get("About.LinkSupportTitle"), "https://github.com/KiYouJyo/ApiMonitor/blob/main/SUPPORT.md"),
        new AboutLinkRow("License", L10n.Get("About.LinkLicenseTitle"), "https://github.com/KiYouJyo/ApiMonitor/blob/main/LICENSE"),
        new AboutLinkRow("ThirdParty", L10n.Get("About.LinkThirdPartyTitle"), "https://github.com/KiYouJyo/ApiMonitor/blob/main/THIRD-PARTY-NOTICES.md"),
    };

    // ---- 更新检查状态 ----
    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private bool _hasUpdateStatus;

    [ObservableProperty]
    private bool _hasUpdateAvailable;

    [ObservableProperty]
    private string _updateAvailableVersion = string.Empty;

    [ObservableProperty]
    private string _updateReleaseUrl = string.Empty;

    [ObservableProperty]
    private bool _isDiagnosticsCopying;

    /// <summary>当前 UI 语言与主题（关于页“当前能力”展示）。</summary>
    public string CurrentLanguageText { get; private set; } = L10n.Format("About.CurrentLanguageFormat", "en-US");

    public string CurrentThemeText { get; private set; } = L10n.Format("About.CurrentThemeFormat", L10n.Get("About.ThemeSystem"));

    public IAsyncRelayCommand CheckUpdatesCommand { get; }

    public IAsyncRelayCommand CopyDiagnosticsCommand { get; }

    public IAsyncRelayCommand<AboutLinkRow> OpenLinkCommand { get; }

    public IAsyncRelayCommand OpenDataFolderCommand { get; }

    public IAsyncRelayCommand ExportBackupCommand { get; }

    public IAsyncRelayCommand ImportBackupCommand { get; }

    /// <summary>打开发布页（更新可用时）。</summary>
    public IAsyncRelayCommand OpenReleasePageCommand { get; }

    public AboutViewModel(
        IReadOnlyList<ProviderInfo> providers,
        IUpdateCheckService updateCheck,
        IDiagnosticsInfoService diagnostics,
        IClipboardService clipboard,
        IExternalLinkLauncher launcher,
        ILocalDataFolderOpener dataFolderOpener,
        IFilePickerService filePicker,
        IPortableBackupService backup,
        string languageCode,
        string themeName,
        AppLog? log = null)
    {
        _updateCheck = updateCheck;
        _diagnostics = diagnostics;
        _clipboard = clipboard;
        _launcher = launcher;
        _dataFolderOpener = dataFolderOpener;
        _filePicker = filePicker;
        _backup = backup;
        _log = log ?? new AppLog(System.IO.Path.GetTempPath());

        // 语言与主题状态来自统一服务，避免默认值错误显示。
        CurrentLanguageText = L10n.Format("About.CurrentLanguageFormat", languageCode);
        CurrentThemeText = L10n.Format("About.CurrentThemeFormat", FormatThemeName(themeName));

        foreach (var provider in providers.OrderBy(p => p.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            Providers.Add(new AboutProviderRow(provider.ProviderId, provider.DisplayName));
        }

        OnPropertyChanged(nameof(ProviderCountText));

        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync, () => !IsCheckingUpdates);
        CopyDiagnosticsCommand = new AsyncRelayCommand(CopyDiagnosticsAsync, () => !IsDiagnosticsCopying);
        OpenLinkCommand = new AsyncRelayCommand<AboutLinkRow>(OpenLinkAsync);
        OpenDataFolderCommand = new AsyncRelayCommand(OpenDataFolderAsync);
        ExportBackupCommand = new AsyncRelayCommand(ExportBackupAsync);
        ImportBackupCommand = new AsyncRelayCommand(ImportBackupAsync);
        OpenReleasePageCommand = new AsyncRelayCommand(OpenReleasePageAsync);
    }

    /// <summary>设置当前语言与主题文本（由视图层或注入方调用）。</summary>
    public void SetEnvironmentTexts(string languageCode, string themeName)
    {
        CurrentLanguageText = L10n.Format("About.CurrentLanguageFormat", languageCode);
        CurrentThemeText = L10n.Format("About.CurrentThemeFormat", FormatThemeName(themeName));
        OnPropertyChanged(nameof(CurrentLanguageText));
        OnPropertyChanged(nameof(CurrentThemeText));
    }

    private static string FormatThemeName(string themeName) =>
        themeName switch
        {
            nameof(AppThemePreference.Light) => L10n.Get("About.ThemeLight"),
            nameof(AppThemePreference.Dark) => L10n.Get("About.ThemeDark"),
            _ => L10n.Get("About.ThemeSystem"),
        };

    private async Task OpenReleasePageAsync()
    {
        if (string.IsNullOrEmpty(UpdateReleaseUrl)
            || !Uri.TryCreate(UpdateReleaseUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        bool ok = await _launcher.LaunchUriAsync(uri);
        if (!ok)
        {
            UpdateStatusText = L10n.Get("About.LinkLaunchFailed");
            HasUpdateStatus = true;
        }
    }

    partial void OnIsCheckingUpdatesChanged(bool value) => CheckUpdatesCommand.NotifyCanExecuteChanged();

    partial void OnIsDiagnosticsCopyingChanged(bool value) => CopyDiagnosticsCommand.NotifyCanExecuteChanged();

    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        HasUpdateAvailable = false;
        UpdateStatusText = L10n.Get("About.CheckingUpdates");
        HasUpdateStatus = true;
        try
        {
            var result = await _updateCheck.CheckAsync(_lifetime.Token);
            switch (result.Status)
            {
                case UpdateCheckStatus.UpToDate:
                    UpdateStatusText = L10n.Get("About.UpToDate");
                    break;
                case UpdateCheckStatus.UpdateAvailable:
                    UpdateAvailableVersion = result.LatestVersion ?? string.Empty;
                    UpdateReleaseUrl = result.ReleaseUrl ?? string.Empty;
                    UpdateStatusText = L10n.Format("About.UpdateAvailableFormat", result.LatestVersion ?? string.Empty);
                    HasUpdateAvailable = true;
                    break;
                case UpdateCheckStatus.DevVersionNewer:
                    UpdateStatusText = L10n.Get("About.DevVersionNewer");
                    break;
                default:
                    UpdateStatusText = L10n.Format("About.UpdateCheckFailedFormat", result.ErrorMessage ?? L10n.Get("Common.Unknown"));
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"检查更新失败: {ex.GetType().Name}");
            UpdateStatusText = L10n.Get("About.UpdateCheckFailed");
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    private async Task CopyDiagnosticsAsync()
    {
        if (IsDiagnosticsCopying)
        {
            return;
        }

        IsDiagnosticsCopying = true;
        try
        {
            string text = await _diagnostics.BuildAsync(_lifetime.Token);
            await _clipboard.SetPlainTextAsync(text, _lifetime.Token);
            UpdateStatusText = L10n.Get("About.DiagnosticsCopied");
            HasUpdateStatus = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"复制诊断信息失败: {ex.GetType().Name}");
            UpdateStatusText = L10n.Get("About.DiagnosticsCopyFailed");
            HasUpdateStatus = true;
        }
        finally
        {
            IsDiagnosticsCopying = false;
        }
    }

    private async Task OpenLinkAsync(AboutLinkRow? link)
    {
        if (link is null || !Uri.TryCreate(link.Url, UriKind.Absolute, out var uri))
        {
            return;
        }

        bool ok = await _launcher.LaunchUriAsync(uri);
        if (!ok)
        {
            UpdateStatusText = L10n.Get("About.LinkLaunchFailed");
            HasUpdateStatus = true;
        }
    }

    private async Task OpenDataFolderAsync()
    {
        bool ok = await _dataFolderOpener.OpenAsync();
        if (!ok)
        {
            UpdateStatusText = L10n.Get("About.OpenDataFolderFailed");
            HasUpdateStatus = true;
        }
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

        try
        {
            await _backup.ExportAsync(path, _lifetime.Token);
            UpdateStatusText = L10n.Get("About.BackupExported");
            HasUpdateStatus = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"导出备份失败: {ex.GetType().Name}");
            UpdateStatusText = L10n.Get("About.BackupExportFailed");
            HasUpdateStatus = true;
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

        try
        {
            var preview = await _backup.InspectAsync(path, _lifetime.Token);
            UpdateStatusText =
                L10n.Format("About.BackupImportPreview", preview.AccountCount, preview.HistoryEntryCount) +
                L10n.Get("About.BackupImportMerge");
            HasUpdateStatus = true;

            // 本版本只实现安全合并（保留本机优先），不实现无提示全量覆盖。
            var result = await _backup.ImportAsync(path, BackupMergePreference.KeepLocal, _lifetime.Token);
            UpdateStatusText =
                L10n.Format("About.BackupImportDone", result.AddedAccounts, result.UpdatedAccounts) +
                L10n.Format("About.BackupImportSkipped", result.SkippedAccounts, result.FailedAccounts) +
                (result.AccountsNeedingCredential.Count > 0
                    ? L10n.Format("About.BackupImportNeedingKey", string.Join("、", result.AccountsNeedingCredential))
                    : string.Empty);
            HasUpdateStatus = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"导入备份失败: {ex.GetType().Name}");
            UpdateStatusText = L10n.Format("About.BackupImportFailed", ex.Message);
            HasUpdateStatus = true;
        }
    }

    public void Shutdown() => _lifetime.Cancel();
}
