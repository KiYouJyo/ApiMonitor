namespace ApiMonitor.Models;

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

/// <summary>
/// 某账户某指标的低余额阈值规则。
/// 规则引用稳定的 <see cref="MetricId"/>，并缓存展示名称与单位。
/// </summary>
public sealed class BalanceThresholdRule
{
    /// <summary>稳定指标 ID（与快照中的 BalanceMetric.MetricId 一致）。</summary>
    public required string MetricId { get; init; }

    /// <summary>指标的展示名称（如“CNY 总余额”“剩余 Credits”）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>指标单位（如 CNY、credits）。</summary>
    public string Unit { get; set; } = string.Empty;

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
