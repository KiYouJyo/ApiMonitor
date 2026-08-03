namespace ApiBalanceMonitor.Models;

/// <summary>
/// 账户级自动刷新设置与低余额阈值规则。
/// 所有持久化时间使用 UTC，界面展示时转换为本地时间。
/// </summary>
public sealed class MonitoringSettings
{
    public bool AutoRefreshEnabled { get; set; } = true;

    public int RefreshIntervalMinutes { get; set; } = MonitoringIntervals.DefaultMinutes;

    public DateTimeOffset? NextRefreshAtUtc { get; set; }

    public List<BalanceThresholdRule> Thresholds { get; set; } = new();
}

/// <summary>允许的自动刷新间隔（分钟）。不允许低于 5 分钟。</summary>
public static class MonitoringIntervals
{
    public const int DefaultMinutes = 30;

    public static readonly IReadOnlyList<int> Options = new[] { 5, 15, 30, 60, 180, 360, 720, 1440 };
}

/// <summary>某账户某币种的低余额阈值规则。</summary>
public sealed class BalanceThresholdRule
{
    public required string Currency { get; init; }

    public bool IsEnabled { get; set; }

    public decimal ThresholdAmount { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public enum ThresholdStatus
{
    Unknown,
    Normal,
    BelowThreshold,
    Disabled,
}
