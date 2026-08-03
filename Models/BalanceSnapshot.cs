namespace ApiMonitor.Models;

/// <summary>
/// 某账户在某时刻的一次余额快照，可包含多个指标/币种。
/// <see cref="SnapshotId"/> 是稳定去重标识：通知引擎用它防止
/// 手动/自动刷新、多窗口事件或应用重启对同一结果重复提醒。
/// </summary>
public sealed class BalanceSnapshot
{
    public required string SnapshotId { get; init; }

    public required string AccountId { get; init; }

    public required string ProviderId { get; init; }

    public bool IsAvailable { get; init; }

    public required DateTimeOffset RetrievedAt { get; init; }

    public required IReadOnlyList<BalanceMetric> Metrics { get; init; }
}
