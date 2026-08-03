using ApiBalanceMonitor.Models;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 余额历史保留策略：默认保留最近 90 天，每账户最多 10000 个快照；
/// 超出时删除最旧记录。纯逻辑，便于单元测试。
/// </summary>
public static class HistoryRetention
{
    public const int MaxEntriesPerAccount = 10_000;

    public const int MaxAgeDays = 90;

    public static IReadOnlyList<BalanceHistoryEntry> Apply(
        IEnumerable<BalanceHistoryEntry> history,
        DateTimeOffset nowUtc)
    {
        DateTimeOffset cutoff = nowUtc.AddDays(-MaxAgeDays);
        return history
            .Where(h => h.SucceededAtUtc >= cutoff)
            .OrderByDescending(h => h.SucceededAtUtc)
            .ThenBy(h => h.Id)
            .Take(MaxEntriesPerAccount)
            .ToList();
    }
}
