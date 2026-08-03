using ApiMonitor.Models;
using ApiMonitor.Providers;

namespace ApiMonitor.Services;

/// <summary>
/// 账户、余额查询与本地数据门面：ViewModel 与调度服务只依赖此接口。
/// </summary>
public interface IAccountManager
{
    /// <summary>某账户开始真实查询时触发（手动与自动共用）。</summary>
    event EventHandler<AccountRefreshStartedEventArgs>? RefreshStarted;

    /// <summary>某账户查询完成时触发（手动与自动共用）。</summary>
    event EventHandler<AccountRefreshCompletedEventArgs>? RefreshCompleted;

    /// <summary>账户集合变化时触发（加载、保存、删除后）。</summary>
    event EventHandler? AccountsChanged;

    IReadOnlyList<ProviderInfo> Providers { get; }

    IReadOnlyList<string> RecoveryMessages { get; }

    Task<IReadOnlyList<ApiAccount>> LoadAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiAccount>> GetAllAccountsAsync(CancellationToken cancellationToken);

    Task<ApiAccount?> GetAccountAsync(string accountId, CancellationToken cancellationToken);

    Task<AccountBalanceRecord?> GetRecordAsync(string accountId, CancellationToken cancellationToken);

    Task<string?> GetApiKeyAsync(string accountId, CancellationToken cancellationToken);

    Task<BalanceQueryResult> TestConnectionAsync(
        string providerId,
        string? apiKey,
        string? accountId,
        CancellationToken cancellationToken);

    Task<ApiAccount> SaveAccountAsync(
        string? accountId,
        string providerId,
        string displayName,
        string? newApiKey,
        MonitoringSettings monitoring,
        CancellationToken cancellationToken);

    Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>手动与自动刷新共用的账户级查询入口（带并发保护）。</summary>
    Task<BalanceQueryResult> RefreshAccountAsync(
        string accountId,
        BalanceQuerySource source,
        CancellationToken cancellationToken);

    /// <summary>返回已启用自动刷新且到期的账户 ID（NextRefreshAtUtc 为空视为到期）。</summary>
    Task<IReadOnlyList<string>> GetAutoRefreshDueAccountIdsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BalanceHistoryEntry>> GetHistoryAsync(
        string accountId,
        CancellationToken cancellationToken);

    /// <summary>只清除该账户的历史记录，不影响账户、凭据、设置与最新余额。</summary>
    Task ClearHistoryAsync(string accountId, CancellationToken cancellationToken);
}
