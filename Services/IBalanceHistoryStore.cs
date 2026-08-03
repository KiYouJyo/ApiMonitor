using ApiBalanceMonitor.Models;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 余额历史文件层接口（历史数据与最新快照存放在同一 records 文件中）。
/// 历史写入由刷新协调流程在单次原子保存中完成。
/// </summary>
public interface IBalanceHistoryStore
{
    Task<IReadOnlyList<BalanceHistoryEntry>> GetHistoryAsync(
        string accountId,
        CancellationToken cancellationToken);

    /// <summary>按保留策略清理超龄/超量记录，返回清理数量。</summary>
    Task<int> PruneAsync(CancellationToken cancellationToken);
}
