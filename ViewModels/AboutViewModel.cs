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

/// <summary>运行状况检查结果行（只含非敏感信息）。</summary>
public sealed record HealthCheckRow(string CheckId, string StatusText, string Message);

/// <summary>
/// v0.6.0：完整“关于”页 ViewModel。
/// v1.0.0：版本、渠道、更新来源全部从 IDistributionChannelService / IUpdateService
/// 读取；渠道行为由构建配置决定，不在 XAML/ViewModel 中硬编码版本或渠道。
/// 版本来自统一元数据服务 AppInfo（不在 XAML 硬编码）；
/// Provider 列表来自 Provider 注册表（不写死）；
/// 更新检查只在用户点击时按渠道执行，不自动下载/安装。
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;
    private readonly IDistributionChannelService _channel;
    private readonly IDiagnosticsInfoService _diagnostics;
    private readonly IAppHealthService _health;
    private readonly IClipboardService _clipboard;
    private readonly IExternalLinkLauncher _launcher;
    private readonly ILocalDataFolderOpener _dataFolderOpener;
    private readonly IFilePickerService _filePicker;
    private readonly IPortableBackupService _backup;
    private readonly Func<CancellationToken, Task<UpdateCheckResult>>? _storeInstallRequest;
    private readonly AppLog? _log;
    private readonly CancellationTokenSource _lifetime = new();

    // ---- 产品信息（来自统一元数据服务） ----
    public string ProductName => "ApiMonitor";

    public string Tagline => L10n.Get("About.TaglineText");

    public string DisplayVersionText => L10n.Format("About.DisplayVersionFormat", AppInfo.DisplayVersion);

    public string PackageVersionText => L10n.Format("About.PackageVersionFormat", AppInfo.PackageVersion);

    public string ArchitectureText => L10n.Format("About.ArchitectureFormat", AppInfo.Architecture);

    public string DistributionChannelText => L10n.Format(
        "About.DistributionChannelFormat",
        FormatChannelName(_channel.CurrentChannel));

    public string UpdateSourceText => L10n.Format(
        "About.UpdateSourceFormat",
        L10n.Get(_channel.UpdateSourceKey));

    public string PackageFamilyText => L10n.Format(
        "About.PackageFamilyFormat",
        string.IsNullOrEmpty(AppInfo.PackageFamilyName)
            ? L10n.Get("About.PackageFamilyNone")
            : AppInfo.PackageFamilyName);

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

    [ObservableProperty]
    private bool _hasStoreInstallAvailable;

    [ObservableProperty]
    private bool _isRunningHealthChecks;

    [ObservableProperty]
    private bool _hasHealthResults;

    [ObservableProperty]
    private string _overallHealthText = string.Empty;

    public ObservableCollection<HealthCheckRow> HealthChecks { get; } = new();

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

    /// <summary>Store 渠道：由用户主动请求下载并安装更新（StoreContext 官方流程）。</summary>
    public IAsyncRelayCommand InstallStoreUpdateCommand { get; }

    public IAsyncRelayCommand RunHealthChecksCommand { get; }

    public IAsyncRelayCommand OpenSupportCommand { get; }

    public AboutViewModel(
        IReadOnlyList<ProviderInfo> providers,
        IUpdateService updateService,
        IDistributionChannelService channel,
        IDiagnosticsInfoService diagnostics,
        IAppHealthService health,
        IClipboardService clipboard,
        IExternalLinkLauncher launcher,
        ILocalDataFolderOpener dataFolderOpener,
        IFilePickerService filePicker,
        IPortableBackupService backup,
        Func<CancellationToken, Task<UpdateCheckResult>>? storeInstallRequest,
        string languageCode,
        string themeName,
        AppLog? log = null)
    {
        _updateService = updateService;
        _channel = channel;
        _diagnostics = diagnostics;
        _health = health;
        _clipboard = clipboard;
        _launcher = launcher;
        _dataFolderOpener = dataFolderOpener;
        _filePicker = filePicker;
        _backup = backup;
        _storeInstallRequest = storeInstallRequest;
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
        InstallStoreUpdateCommand = new AsyncRelayCommand(InstallStoreUpdateAsync, () => HasStoreInstallAvailable);
        RunHealthChecksCommand = new AsyncRelayCommand(RunHealthChecksAsync, () => !IsRunningHealthChecks);
        OpenSupportCommand = new AsyncRelayCommand(OpenSupportAsync);
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

    /// <summary>渠道名称（非敏感元数据）。</summary>
    public static string FormatChannelName(DistributionChannel channel) => channel switch
    {
        DistributionChannel.MicrosoftStore => L10n.Get("Channel.MicrosoftStore"),
        DistributionChannel.GitHubSideload => L10n.Get("Channel.GitHubSideload"),
        _ => L10n.Get("Channel.Development"),
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

    partial void OnHasStoreInstallAvailableChanged(bool value) => InstallStoreUpdateCommand.NotifyCanExecuteChanged();

    partial void OnIsRunningHealthChecksChanged(bool value) => RunHealthChecksCommand.NotifyCanExecuteChanged();

    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        HasUpdateAvailable = false;
        HasStoreInstallAvailable = false;
        UpdateStatusText = L10n.Get("About.CheckingUpdates");
        HasUpdateStatus = true;
        try
        {
            var result = await _updateService.CheckAsync(_lifetime.Token);
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
                    HasStoreInstallAvailable = result.CanInstallFromStore && _storeInstallRequest is not null;
                    break;
                case UpdateCheckStatus.DevVersionNewer:
                    UpdateStatusText = L10n.Get("About.DevVersionNewer");
                    break;
                case UpdateCheckStatus.DevelopmentBuild:
                    UpdateStatusText = L10n.Get("About.DevelopmentBuild");
                    break;
                case UpdateCheckStatus.UnsupportedChannel:
                    UpdateStatusText = L10n.Get("About.UpdateUnsupportedChannel");
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

    private async Task InstallStoreUpdateAsync()
    {
        if (_storeInstallRequest is null || IsCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        try
        {
            UpdateStatusText = L10n.Get("About.InstallingStoreUpdate");
            HasUpdateStatus = true;
            var result = await _storeInstallRequest(_lifetime.Token);
            UpdateStatusText = result.Status == UpdateCheckStatus.UpToDate
                ? L10n.Get("About.StoreUpdateInstalled")
                : L10n.Format("About.UpdateCheckFailedFormat", result.ErrorMessage ?? L10n.Get("Common.Unknown"));
            HasUpdateStatus = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"请求 Store 更新失败: {ex.GetType().Name}");
            UpdateStatusText = L10n.Get("About.StoreInstallFailed");
            HasUpdateStatus = true;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    private async Task RunHealthChecksAsync()
    {
        if (IsRunningHealthChecks)
        {
            return;
        }

        IsRunningHealthChecks = true;
        try
        {
            var results = await _health.RunAsync(_lifetime.Token);
            HealthChecks.Clear();
            foreach (var result in results.OrderBy(r => r.CheckId, StringComparer.Ordinal))
            {
                HealthChecks.Add(new HealthCheckRow(
                    result.CheckId,
                    FormatHealthStatus(result.Status),
                    result.Message));
            }

            int failed = results.Count(r => r.Status == HealthStatus.Failed);
            int warnings = results.Count(r => r.Status == HealthStatus.Warning);
            OverallHealthText = failed == 0 && warnings == 0
                ? L10n.Get("Health.OverallOk")
                : L10n.Format("Health.OverallIssues", warnings, failed);
            HasHealthResults = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"运行状况检查失败: {ex.GetType().Name}");
            OverallHealthText = L10n.Get("Health.RunFailed");
            HasHealthResults = true;
        }
        finally
        {
            IsRunningHealthChecks = false;
        }
    }

    private static string FormatHealthStatus(HealthStatus status) => status switch
    {
        HealthStatus.Ok => L10n.Get("Health.StatusOk"),
        HealthStatus.Warning => L10n.Get("Health.StatusWarning"),
        HealthStatus.Failed => L10n.Get("Health.StatusFailed"),
        _ => L10n.Get("Health.StatusNotApplicable"),
    };

    private async Task OpenSupportAsync()
    {
        if (!Uri.TryCreate(_channel.SupportPageUrl, UriKind.Absolute, out var uri))
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
