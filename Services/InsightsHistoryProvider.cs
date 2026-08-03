using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// v0.6.0：数据洞察历史查询门面。只在进入数据洞察页或切换账户时加载历史；
/// 支持按 AccountId、时间范围查询；旧请求通过 CancellationToken 取消；
/// 不阻塞 UI 线程；新快照写入后只失效相关账户缓存（本接口按需读取，
/// 天然避免启动时加载全部历史）。
/// </summary>
public interface IInsightsHistoryProvider
{
    /// <summary>
    /// 返回指定账户按时间倒序的历史（含所有指标）。
    /// </summary>
    Task<IReadOnlyList<BalanceHistoryEntry>> GetHistoryAsync(
        string accountId,
        CancellationToken cancellationToken);
}

public sealed class InsightsHistoryProvider : IInsightsHistoryProvider
{
    private readonly IBalanceHistoryStore _store;

    public InsightsHistoryProvider(IBalanceHistoryStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<BalanceHistoryEntry>> GetHistoryAsync(
        string accountId,
        CancellationToken cancellationToken) =>
        _store.GetHistoryAsync(accountId, cancellationToken);
}
