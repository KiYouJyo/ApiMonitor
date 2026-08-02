namespace ApiBalanceMonitor.Models;

/// <summary>
/// 与账户关联的本地余额记录：最近尝试/成功时间与最后一次成功快照。
/// </summary>
public sealed class AccountBalanceRecord
{
    public required string AccountId { get; init; }

    public required string ProviderId { get; init; }

    public DateTimeOffset? LastQueryAttemptAt { get; set; }

    public DateTimeOffset? LastQuerySuccessAt { get; set; }

    public BalanceSnapshot? LastSuccessfulSnapshot { get; set; }
}
