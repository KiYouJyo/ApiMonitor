namespace ApiBalanceMonitor.Models;

public enum BalanceQuerySource
{
    Manual,
    Automatic,
}

/// <summary>
/// 一次成功查询产生的余额历史快照（同一时间点的多币种归为一个快照）。
/// 不保存 API Key、Authorization 请求头或 API 原始响应。
/// </summary>
public sealed class BalanceHistoryEntry
{
    public required string Id { get; init; }

    public required string AccountId { get; init; }

    public required string ProviderId { get; init; }

    public required DateTimeOffset SucceededAtUtc { get; init; }

    public required BalanceQuerySource Source { get; init; }

    public bool IsAvailable { get; init; }

    public required IReadOnlyList<BalanceAmount> Balances { get; init; }
}
