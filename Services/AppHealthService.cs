using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.ViewModels;

namespace ApiMonitor.Services;

/// <summary>单项运行状况级别。</summary>
public enum HealthStatus
{
    Ok,
    Warning,
    Failed,
    NotApplicable,
}

/// <summary>单项运行状况结果（只含非敏感信息）。</summary>
public sealed record HealthCheckResult(
    string CheckId,
    HealthStatus Status,
    string Message);

/// <summary>
/// v1.0.0：应用运行状况检查。只读取非敏感状态；单项异常被捕获为
/// “失败/注意”而不崩溃；结果不含 API Key、余额、历史、账户名、
/// 完整本地路径、Credential Locker Resource 明细、证书私钥信息。
/// </summary>
public interface IAppHealthService
{
    Task<IReadOnlyList<HealthCheckResult>> RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 21 项运行状况检查实现。每个检查独立 try/catch，单项失败不阻断其他检查；
/// 检查间不共享可崩溃状态。
/// </summary>
public sealed class AppHealthService : IAppHealthService
{
    private readonly IDistributionChannelService _channel;
    private readonly ISecretStore _secretStore;
    private readonly IAccountStore _accountStore;
    private readonly IBalanceSnapshotStore _snapshotStore;
    private readonly IAppearanceSettingsStore _appearanceStore;
    private readonly ProviderRegistry _registry;
    private readonly IAppNotificationService _notificationService;
    private readonly Func<CancellationToken, Task<bool>> _notificationsEnabledReader;
    private readonly ITrayIconService _tray;
    private readonly IStartupTaskService _startupTask;
    private readonly IMonitoringScheduler _scheduler;
    private readonly Func<bool> _mainWindowOpenReader;
    private readonly Func<bool> _floatingWindowOpenReader;
    private readonly IAccountManager _accounts;
    private readonly IUpdateService _updateService;

    public AppHealthService(
        IDistributionChannelService channel,
        ISecretStore secretStore,
        IAccountStore accountStore,
        IBalanceSnapshotStore snapshotStore,
        IAppearanceSettingsStore appearanceStore,
        ProviderRegistry registry,
        IAppNotificationService notificationService,
        Func<CancellationToken, Task<bool>> notificationsEnabledReader,
        ITrayIconService tray,
        IStartupTaskService startupTask,
        IMonitoringScheduler scheduler,
        Func<bool> mainWindowOpenReader,
        Func<bool> floatingWindowOpenReader,
        IAccountManager accounts,
        IUpdateService updateService)
    {
        _channel = channel;
        _secretStore = secretStore;
        _accountStore = accountStore;
        _snapshotStore = snapshotStore;
        _appearanceStore = appearanceStore;
        _registry = registry;
        _notificationService = notificationService;
        _notificationsEnabledReader = notificationsEnabledReader;
        _tray = tray;
        _startupTask = startupTask;
        _scheduler = scheduler;
        _mainWindowOpenReader = mainWindowOpenReader;
        _floatingWindowOpenReader = floatingWindowOpenReader;
        _accounts = accounts;
        _updateService = updateService;
    }

    public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(CancellationToken cancellationToken)
    {
        var results = new List<HealthCheckResult>();
        results.Add(CheckChannel());
        results.Add(CheckDisplayVersion());
        results.Add(CheckPackageVersion());
        results.Add(CheckPackageIdentity());
        results.Add(CheckPackageFamily());
        results.Add(CheckArchitecture());
        results.Add(CheckCredentialLocker());
        results.Add(await CheckAccountsFileAsync(cancellationToken));
        results.Add(await CheckRecordsFileAsync(cancellationToken));
        results.Add(await CheckSettingsFileAsync(cancellationToken));
        results.Add(CheckProviderRegistry());
        results.Add(CheckProviderLoaded("deepseek", L10n.Get("Health.ProviderDeepSeek")));
        results.Add(CheckProviderLoaded("openrouter", L10n.Get("Health.ProviderOpenRouter")));
        results.Add(CheckNotificationRegistered());
        results.Add(await CheckNotificationSystemAsync(cancellationToken));
        results.Add(CheckTray());
        results.Add(await CheckStartupTaskAsync(cancellationToken));
        results.Add(CheckScheduler());
        results.Add(CheckWindows());
        results.Add(await CheckLastQueryAsync(cancellationToken));
        results.Add(CheckUpdateServiceMatchesChannel());
        return results;
    }

    private HealthCheckResult CheckChannel()
    {
        try
        {
            string name = AboutViewModel.FormatChannelName(_channel.CurrentChannel);
            return new HealthCheckResult("channel", HealthStatus.Ok, L10n.Format("Health.ChannelOk", name));
        }
        catch (Exception)
        {
            return new HealthCheckResult("channel", HealthStatus.Failed, L10n.Get("Health.UnknownError"));
        }
    }

    private HealthCheckResult CheckDisplayVersion()
    {
        try
        {
            string version = _channel.DisplayVersion;
            return string.IsNullOrWhiteSpace(version)
                ? new HealthCheckResult("display-version", HealthStatus.Warning, L10n.Get("Health.DisplayVersionMissing"))
                : new HealthCheckResult("display-version", HealthStatus.Ok, L10n.Format("Health.DisplayVersionOk", version));
        }
        catch (Exception)
        {
            return new HealthCheckResult("display-version", HealthStatus.Failed, L10n.Get("Health.UnknownError"));
        }
    }

    private HealthCheckResult CheckPackageVersion()
    {
        try
        {
            string version = _channel.PackageVersion;
            bool valid = version.Split('.').Length == 4
                && version.Split('.').All(part => int.TryParse(part, out _));
            return valid
                ? new HealthCheckResult("package-version", HealthStatus.Ok, L10n.Format("Health.PackageVersionOk", version))
                : new HealthCheckResult("package-version", HealthStatus.Warning, L10n.Get("Health.PackageVersionInvalid"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("package-version", HealthStatus.Failed, L10n.Get("Health.UnknownError"));
        }
    }

    private HealthCheckResult CheckPackageIdentity()
    {
        try
        {
            if (_channel.CurrentChannel == DistributionChannel.Development)
            {
                return new HealthCheckResult("package-identity", HealthStatus.NotApplicable, L10n.Get("Health.IdentityNotApplicable"));
            }

            return _channel.InstalledIdentityMatchesChannel
                ? new HealthCheckResult(
                    "package-identity",
                    HealthStatus.Ok,
                    L10n.Format("Health.IdentityOk", AppInfo.PackageIdentity))
                : new HealthCheckResult(
                    "package-identity",
                    HealthStatus.Warning,
                    L10n.Format("Health.IdentityMismatch", AppInfo.PackageIdentity, _channel.ExpectedIdentityName));
        }
        catch (Exception)
        {
            return new HealthCheckResult("package-identity", HealthStatus.Failed, L10n.Get("Health.UnknownError"));
        }
    }

    private HealthCheckResult CheckPackageFamily()
    {
        try
        {
            if (!AppInfo.IsPackaged)
            {
                return new HealthCheckResult("package-family", HealthStatus.NotApplicable, L10n.Get("Health.PackageFamilyNotApplicable"));
            }

            return string.IsNullOrEmpty(AppInfo.PackageFamilyName)
                ? new HealthCheckResult("package-family", HealthStatus.Failed, L10n.Get("Health.PackageFamilyMissing"))
                : new HealthCheckResult("package-family", HealthStatus.Ok, L10n.Format("Health.PackageFamilyOk", AppInfo.PackageFamilyName));
        }
        catch (Exception)
        {
            return new HealthCheckResult("package-family", HealthStatus.Failed, L10n.Get("Health.UnknownError"));
        }
    }

    private HealthCheckResult CheckArchitecture()
    {
        try
        {
            return string.Equals(AppInfo.Architecture, "X64", StringComparison.OrdinalIgnoreCase)
                ? new HealthCheckResult("architecture", HealthStatus.Ok, L10n.Format("Health.ArchitectureOk", AppInfo.Architecture))
                : new HealthCheckResult("architecture", HealthStatus.Warning, L10n.Format("Health.ArchitectureUnexpected", AppInfo.Architecture));
        }
        catch (Exception)
        {
            return new HealthCheckResult("architecture", HealthStatus.Failed, L10n.Get("Health.UnknownError"));
        }
    }

    private HealthCheckResult CheckCredentialLocker()
    {
        try
        {
            return _secretStore.IsAvailable()
                ? new HealthCheckResult("credential-locker", HealthStatus.Ok, L10n.Get("Health.CredentialLockerOk"))
                : new HealthCheckResult("credential-locker", HealthStatus.Failed, L10n.Get("Health.CredentialLockerUnavailable"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("credential-locker", HealthStatus.Failed, L10n.Get("Health.CredentialLockerUnavailable"));
        }
    }

    private async Task<HealthCheckResult> CheckAccountsFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _accountStore.LoadAsync(cancellationToken);
            return string.IsNullOrEmpty(result.RecoveryMessage)
                ? new HealthCheckResult("accounts-file", HealthStatus.Ok, L10n.Get("Health.AccountsFileOk"))
                : new HealthCheckResult("accounts-file", HealthStatus.Warning, L10n.Get("Health.AccountsFileRecovered"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("accounts-file", HealthStatus.Failed, L10n.Get("Health.AccountsFileUnreadable"));
        }
    }

    private async Task<HealthCheckResult> CheckRecordsFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _snapshotStore.LoadAsync(cancellationToken);
            return string.IsNullOrEmpty(result.RecoveryMessage)
                ? new HealthCheckResult("records-file", HealthStatus.Ok, L10n.Get("Health.RecordsFileOk"))
                : new HealthCheckResult("records-file", HealthStatus.Warning, L10n.Get("Health.RecordsFileRecovered"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("records-file", HealthStatus.Failed, L10n.Get("Health.RecordsFileUnreadable"));
        }
    }

    private async Task<HealthCheckResult> CheckSettingsFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 设置存储内部对损坏文件自动备份并回退默认值；成功返回即视为可解析。
            await _appearanceStore.LoadAsync(cancellationToken);
            return new HealthCheckResult("settings-file", HealthStatus.Ok, L10n.Get("Health.SettingsFileOk"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("settings-file", HealthStatus.Failed, L10n.Get("Health.SettingsFileUnreadable"));
        }
    }

    private HealthCheckResult CheckProviderRegistry()
    {
        try
        {
            int count = _registry.Infos.Count;
            return count > 0
                ? new HealthCheckResult("provider-registry", HealthStatus.Ok, L10n.Format("Health.ProviderRegistryOk", count))
                : new HealthCheckResult("provider-registry", HealthStatus.Failed, L10n.Get("Health.ProviderRegistryEmpty"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("provider-registry", HealthStatus.Failed, L10n.Get("Health.UnknownError"));
        }
    }

    private HealthCheckResult CheckProviderLoaded(string providerId, string displayName)
    {
        try
        {
            return _registry.GetById(providerId) is not null
                ? new HealthCheckResult($"{providerId}-provider", HealthStatus.Ok, L10n.Format("Health.ProviderLoaded", displayName))
                : new HealthCheckResult($"{providerId}-provider", HealthStatus.Failed, L10n.Format("Health.ProviderMissing", displayName));
        }
        catch (Exception)
        {
            return new HealthCheckResult($"{providerId}-provider", HealthStatus.Failed, L10n.Format("Health.ProviderMissing", displayName));
        }
    }

    private HealthCheckResult CheckNotificationRegistered()
    {
        try
        {
            return _notificationService.IsRegistered
                ? new HealthCheckResult("notification-registered", HealthStatus.Ok, L10n.Get("Health.NotificationRegistered"))
                : new HealthCheckResult("notification-registered", HealthStatus.Warning, L10n.Get("Health.NotificationNotRegistered"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("notification-registered", HealthStatus.Warning, L10n.Get("Health.NotificationNotRegistered"));
        }
    }

    private async Task<HealthCheckResult> CheckNotificationSystemAsync(CancellationToken cancellationToken)
    {
        try
        {
            bool enabled = await _notificationsEnabledReader(cancellationToken);
            if (!enabled)
            {
                return new HealthCheckResult("notification-system", HealthStatus.Ok, L10n.Get("Health.NotificationNotEnabled"));
            }

            return _notificationService.IsRegistered
                ? new HealthCheckResult("notification-system", HealthStatus.Ok, L10n.Get("Health.NotificationSystemOk"))
                : new HealthCheckResult("notification-system", HealthStatus.Warning, L10n.Get("Health.NotificationSystemLikelyDisabled"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("notification-system", HealthStatus.Warning, L10n.Get("Health.NotificationSystemUnknown"));
        }
    }

    private HealthCheckResult CheckTray()
    {
        try
        {
            return _tray.IsActive
                ? new HealthCheckResult("tray", HealthStatus.Ok, L10n.Get("Health.TrayOk"))
                : new HealthCheckResult("tray", HealthStatus.Failed, L10n.Get("Health.TrayNotRunning"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("tray", HealthStatus.Failed, L10n.Get("Health.TrayNotRunning"));
        }
    }

    private async Task<HealthCheckResult> CheckStartupTaskAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _startupTask.RefreshStatusAsync(cancellationToken);
            return status switch
            {
                Models.StartupTaskStatus.Enabled => new HealthCheckResult("startup-task", HealthStatus.Ok, L10n.Get("Health.StartupTaskEnabled")),
                Models.StartupTaskStatus.Disabled => new HealthCheckResult("startup-task", HealthStatus.Ok, L10n.Get("Health.StartupTaskDisabled")),
                Models.StartupTaskStatus.DisabledByUser => new HealthCheckResult("startup-task", HealthStatus.Warning, L10n.Get("Health.StartupTaskDisabledByUser")),
                _ => new HealthCheckResult("startup-task", HealthStatus.Warning, L10n.Get("Health.StartupTaskUnknown")),
            };
        }
        catch (Exception)
        {
            return new HealthCheckResult("startup-task", HealthStatus.Warning, L10n.Get("Health.StartupTaskUnknown"));
        }
    }

    private HealthCheckResult CheckScheduler()
    {
        try
        {
            return _scheduler.IsRunning
                ? new HealthCheckResult("scheduler", HealthStatus.Ok, L10n.Get("Health.SchedulerOk"))
                : new HealthCheckResult("scheduler", HealthStatus.Warning, L10n.Get("Health.SchedulerNotRunning"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("scheduler", HealthStatus.Warning, L10n.Get("Health.SchedulerNotRunning"));
        }
    }

    private HealthCheckResult CheckWindows()
    {
        try
        {
            bool main = _mainWindowOpenReader();
            bool floating = _floatingWindowOpenReader();
            return main
                ? new HealthCheckResult(
                    "windows",
                    HealthStatus.Ok,
                    floating ? L10n.Get("Health.WindowsWithFloating") : L10n.Get("Health.WindowsOk"))
                : new HealthCheckResult("windows", HealthStatus.Warning, L10n.Get("Health.WindowsMainMissing"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("windows", HealthStatus.Warning, L10n.Get("Health.WindowsMainMissing"));
        }
    }

    private async Task<HealthCheckResult> CheckLastQueryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var accounts = await _accounts.GetAllAccountsAsync(cancellationToken);
            if (accounts.Count == 0)
            {
                return new HealthCheckResult("last-query", HealthStatus.NotApplicable, L10n.Get("Health.LastQueryNoAccounts"));
            }

            DateTimeOffset? latest = null;
            foreach (var account in accounts)
            {
                var record = await _accounts.GetRecordAsync(account.AccountId, cancellationToken);
                if (record?.LastQuerySuccessAt is { } successAt
                    && (latest is null || successAt > latest))
                {
                    latest = successAt;
                }
            }

            return latest is { } time
                ? new HealthCheckResult(
                    "last-query",
                    HealthStatus.Ok,
                    L10n.Format("Health.LastQueryOk", time.ToLocalTime().ToString("yyyy-MM-dd HH:mm")))
                : new HealthCheckResult("last-query", HealthStatus.Warning, L10n.Get("Health.LastQueryNeverSucceeded"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("last-query", HealthStatus.Warning, L10n.Get("Health.LastQueryUnknown"));
        }
    }

    private HealthCheckResult CheckUpdateServiceMatchesChannel()
    {
        try
        {
            return _updateService.Channel == _channel.CurrentChannel
                ? new HealthCheckResult("update-service", HealthStatus.Ok, L10n.Get("Health.UpdateServiceMatches"))
                : new HealthCheckResult("update-service", HealthStatus.Failed, L10n.Get("Health.UpdateServiceMismatch"));
        }
        catch (Exception)
        {
            return new HealthCheckResult("update-service", HealthStatus.Failed, L10n.Get("Health.UpdateServiceMismatch"));
        }
    }
}
