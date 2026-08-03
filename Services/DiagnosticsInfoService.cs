using System.Text;

namespace ApiMonitor.Services;

/// <summary>
/// v0.6.0：诊断信息构建。只包含非敏感元数据：
/// DisplayVersion、PackageVersion、架构、Windows 版本、.NET 版本、
/// Windows App SDK 版本、Package Family、数据 schema 版本、
/// Provider ID 列表、账户数量、通知注册状态、StartupTask 状态、语言与主题。
/// 禁止包含：API Key、Account DisplayName、账户余额、历史、Credential Locker
/// Resource、用户名、计算机名、完整本地文件路径、证书私钥信息、Authorization。
/// </summary>
public interface IDiagnosticsInfoService
{
    Task<string> BuildAsync(CancellationToken cancellationToken);
}

public sealed class DiagnosticsInfoService : IDiagnosticsInfoService
{
    private readonly IAccountManager _accountManager;
    private readonly INotificationStateStore _notificationStateStore;
    private readonly IStartupTaskService _startupTaskService;
    private readonly string _language;
    private readonly string _theme;

    public DiagnosticsInfoService(
        IAccountManager accountManager,
        INotificationStateStore notificationStateStore,
        IStartupTaskService startupTaskService,
        string language,
        string theme)
    {
        _accountManager = accountManager;
        _notificationStateStore = notificationStateStore;
        _startupTaskService = startupTaskService;
        _language = language;
        _theme = theme;
    }

    public async Task<string> BuildAsync(CancellationToken cancellationToken)
    {
        var accounts = await _accountManager.GetAllAccountsAsync(cancellationToken);
        var notificationStates = await _notificationStateStore.LoadAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("ApiMonitor Diagnostics");
        sb.AppendLine($"DisplayVersion: {AppInfo.DisplayVersion}");
        sb.AppendLine($"PackageVersion: {AppInfo.PackageVersion}");
        sb.AppendLine($"Architecture: {AppInfo.Architecture}");
        sb.AppendLine($"Windows: {AppInfo.WindowsVersion}");
        sb.AppendLine($".NET Runtime: {AppInfo.DotNetRuntimeVersion}");
        sb.AppendLine($"Windows App SDK: {AppInfo.WindowsAppSdkVersion}");
        sb.AppendLine($"PackageFamily: {(string.IsNullOrEmpty(AppInfo.PackageFamilyName) ? "(unpackaged)" : AppInfo.PackageFamilyName)}");
        sb.AppendLine($"AccountSchemaVersion: {JsonAccountStore.CurrentSchemaVersion}");
        sb.AppendLine($"BalanceRecordsSchemaVersion: {JsonBalanceSnapshotStore.CurrentSchemaVersion}");
        sb.AppendLine($"ProviderIds: {string.Join(",", _accountManager.Providers.Select(p => p.ProviderId))}");
        sb.AppendLine($"AccountCount: {accounts.Count}");
        sb.AppendLine($"NotificationStatesTracked: {notificationStates.Count}");
        sb.AppendLine($"StartupTask: {_startupTaskService.CachedStatus?.ToString() ?? "Unknown"}");
        sb.AppendLine($"Language: {_language}");
        sb.AppendLine($"Theme: {_theme}");
        return sb.ToString();
    }
}
