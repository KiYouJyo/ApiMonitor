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
    private readonly AppLog _log;
    private readonly CancellationTokenSource _lifetime = new();

    // ---- 产品信息（来自统一元数据服务） ----
    public string ProductName => "ApiMonitor";

    public string Tagline => "查询并记录你自己的 API 账户余额";

    public string DisplayVersionText => $"版本：v{AppInfo.DisplayVersion}";

    public string PackageVersionText => $"包版本：{AppInfo.PackageVersion}";

    public string ArchitectureText => $"架构：{AppInfo.Architecture}";

    public string PackageIdentityText => $"Package Identity：{AppInfo.PackageIdentity}";

    public string PublisherText => $"发布者：{AppInfo.Publisher}";

    public string LicenseText => "MIT License";

    public string CopyrightText => "Copyright (c) 2026 KiYouJyo";

    // ---- 当前能力（动态，来自注册表） ----
    public ObservableCollection<AboutProviderRow> Providers { get; } = new();

    public string ProviderCountText => $"当前 Provider 数量：{Providers.Count}";

    // ---- 隐私与安全摘要 ----
    public IReadOnlyList<string> PrivacyItems { get; } = new[]
    {
        "API Key 保存在 Windows Credential Locker。",
        "余额、历史和设置保存在本机。",
        "应用没有开发者云端服务器。",
        "没有遥测和广告。",
        "通知由本机生成。",
        "便携备份不包含 API Key。",
    };

    // ---- 项目链接 ----
    public IReadOnlyList<AboutLinkRow> Links { get; } = new[]
    {
        new AboutLinkRow("Repository", "GitHub 仓库", "https://github.com/KiYouJyo/ApiMonitor"),
        new AboutLinkRow("Releases", "Releases", "https://github.com/KiYouJyo/ApiMonitor/releases"),
        new AboutLinkRow("Issues", "提交问题", "https://github.com/KiYouJyo/ApiMonitor/issues"),
        new AboutLinkRow("Privacy", "隐私政策", "https://github.com/KiYouJyo/ApiMonitor/blob/main/PRIVACY.md"),
        new AboutLinkRow("Security", "安全政策", "https://github.com/KiYouJyo/ApiMonitor/blob/main/SECURITY.md"),
        new AboutLinkRow("Support", "支持文档", "https://github.com/KiYouJyo/ApiMonitor/blob/main/SUPPORT.md"),
        new AboutLinkRow("License", "MIT License", "https://github.com/KiYouJyo/ApiMonitor/blob/main/LICENSE"),
        new AboutLinkRow("ThirdParty", "第三方声明", "https://github.com/KiYouJyo/ApiMonitor/blob/main/THIRD-PARTY-NOTICES.md"),
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
    public string CurrentLanguageText { get; private set; } = "当前 UI 语言：en-US";

    public string CurrentThemeText { get; private set; } = "当前主题：跟随系统";

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
        AppLog? log = null)
    {
        _updateCheck = updateCheck;
        _diagnostics = diagnostics;
        _clipboard = clipboard;
        _launcher = launcher;
        _dataFolderOpener = dataFolderOpener;
        _filePicker = filePicker;
        _backup = backup;
        _log = log;

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
        CurrentLanguageText = $"当前 UI 语言：{languageCode}";
        CurrentThemeText = $"当前主题：{themeName}";
        OnPropertyChanged(nameof(CurrentLanguageText));
        OnPropertyChanged(nameof(CurrentThemeText));
    }

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
            UpdateStatusText = "无法打开链接，请稍后重试。";
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
        UpdateStatusText = "正在检查更新…";
        HasUpdateStatus = true;
        try
        {
            var result = await _updateCheck.CheckAsync(_lifetime.Token);
            switch (result.Status)
            {
                case UpdateCheckStatus.UpToDate:
                    UpdateStatusText = "已是最新版本。";
                    break;
                case UpdateCheckStatus.UpdateAvailable:
                    UpdateAvailableVersion = result.LatestVersion ?? string.Empty;
                    UpdateReleaseUrl = result.ReleaseUrl ?? string.Empty;
                    UpdateStatusText = $"发现新版本 v{result.LatestVersion}。";
                    HasUpdateAvailable = true;
                    break;
                case UpdateCheckStatus.DevVersionNewer:
                    UpdateStatusText = "当前为较新的开发版本。";
                    break;
                default:
                    UpdateStatusText = $"检查更新失败：{result.ErrorMessage ?? "未知错误"}";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"检查更新失败: {ex.GetType().Name}");
            UpdateStatusText = "检查更新失败，请稍后重试。";
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
            UpdateStatusText = "诊断信息已复制到剪贴板。";
            HasUpdateStatus = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"复制诊断信息失败: {ex.GetType().Name}");
            UpdateStatusText = "复制诊断信息失败。";
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
            UpdateStatusText = "无法打开链接，请稍后重试。";
            HasUpdateStatus = true;
        }
    }

    private async Task OpenDataFolderAsync()
    {
        bool ok = await _dataFolderOpener.OpenAsync();
        if (!ok)
        {
            UpdateStatusText = "无法打开本地数据文件夹。";
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
            UpdateStatusText = "便携备份已导出（不含 API Key）。";
            HasUpdateStatus = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"导出备份失败: {ex.GetType().Name}");
            UpdateStatusText = "导出备份失败，请稍后重试。";
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
                $"备份可导入：{preview.AccountCount} 个账户、{preview.HistoryEntryCount} 条历史。" +
                "导入为安全合并，已有账户保留本机凭据；新账户需要重新输入 API Key。";
            HasUpdateStatus = true;

            // 本版本只实现安全合并（保留本机优先），不实现无提示全量覆盖。
            var result = await _backup.ImportAsync(path, BackupMergePreference.KeepLocal, _lifetime.Token);
            UpdateStatusText =
                $"导入完成：新增 {result.AddedAccounts}、更新 {result.UpdatedAccounts}、" +
                $"跳过 {result.SkippedAccounts}、失败 {result.FailedAccounts}。" +
                (result.AccountsNeedingCredential.Count > 0
                    ? $" 以下账户需要重新输入 API Key：{string.Join("、", result.AccountsNeedingCredential)}"
                    : string.Empty);
            HasUpdateStatus = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"导入备份失败: {ex.GetType().Name}");
            UpdateStatusText = $"导入失败：{ex.Message}（已回滚本次变更）。";
            HasUpdateStatus = true;
        }
    }

    public void Shutdown() => _lifetime.Cancel();
}
