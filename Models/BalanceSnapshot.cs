namespace ApiBalanceMonitor.Models;

/// <summary>
/// 某账户在某时刻的一次余额快照，可包含多个币种。
/// </summary>
public sealed class BalanceSnapshot
{
    public required string AccountId { get; init; }

    public required string ProviderId { get; init; }

    public bool IsAvailable { get; init; }

    public required DateTimeOffset RetrievedAt { get; init; }

    public required IReadOnlyList<BalanceAmount> Balances { get; init; }
}

/// <summary>
/// 单个币种的余额明细。金额一律使用 <see cref="decimal"/>。
/// </summary>
public sealed class BalanceAmount
{
    public required string Currency { get; init; }

    public decimal TotalBalance { get; init; }

    public decimal GrantedBalance { get; init; }

    public decimal ToppedUpBalance { get; init; }
}
