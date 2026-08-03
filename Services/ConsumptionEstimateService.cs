using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>估算不可用的原因（界面显示明确原因，不伪造预测）。</summary>
public enum EstimateUnavailableReason
{
    /// <summary>有效消耗区间不足（少于 3 个）。</summary>
    NotEnoughData,

    /// <summary>数据跨越时间不足 24 小时。</summary>
    TimeSpanTooShort,

    /// <summary>尚未观察到余额下降。</summary>
    NoConsumption,

    /// <summary>最近主要为充值/重置，无法形成稳定消耗。</summary>
    RecentTopUpOrReset,

    /// <summary>该指标类型不支持预测。</summary>
    UnsupportedMetric,

    /// <summary>当前余额未知。</summary>
    UnknownCurrentValue,

    /// <summary>日消耗不为正（当前值≤0 或估算异常）。</summary>
    NonPositiveConsumption,
}

/// <summary>消费估算结果：每日消耗与预计可用天数。</summary>
public sealed class ConsumptionEstimate
{
    /// <summary>估算是否可用。</summary>
    public bool IsAvailable { get; init; }

    /// <summary>不可用原因（IsAvailable=false 时）。</summary>
    public EstimateUnavailableReason? UnavailableReason { get; init; }

    /// <summary>每日消耗（正数表示消耗；IsAvailable=false 时为 0）。</summary>
    public decimal DailyConsumption { get; init; }

    /// <summary>预计可用天数（IsAvailable=false 或为无限额度时为 null）。</summary>
    public decimal? EstimatedDaysLeft { get; init; }

    /// <summary>参与估算的有效消耗区间数。</summary>
    public int ValidIntervals { get; init; }

    /// <summary>估算所用数据跨越的起始时间（UTC）。</summary>
    public DateTimeOffset? DataStartUtc { get; init; }

    /// <summary>估算所用数据跨越的结束时间（UTC）。</summary>
    public DateTimeOffset? DataEndUtc { get; init; }
}

/// <summary>
/// v0.6.0：消费估算纯逻辑服务。只对“剩余余额/剩余 Credits”类指标估算：
///   - 对按时间排序的连续快照计算 previousValue - currentValue（下降才有效）；
///   - 除以两点之间的实际天数；忽略上涨/充值/重置与零时间间隔；
///   - 采用有效区间日消耗率的中位数，减少单次异常值影响；
///   - 至少 3 个有效区间、跨度 ≥24 小时、当前值有效且日消耗 &gt; 0。
/// 累计使用量（Usage）类指标不计算“预计可用天数”。
/// </summary>
public interface IConsumptionEstimateService
{
    /// <summary>
    /// 估算指定账户在时间范围内指标快照的每日消耗与预计可用天数。
    /// points 已按时间升序排列，每个点包含时间与数值（null 表示未知，跳过）。
    /// </summary>
    ConsumptionEstimate Estimate(
        IReadOnlyList<TimePoint> points,
        BalanceMetricKind metricKind,
        decimal? currentAvailable,
        bool isUnlimited);
}

/// <summary>趋势图/估算共用的时间点（数值未知时为 null）。</summary>
public readonly record struct TimePoint(DateTimeOffset TimeUtc, decimal? Value);

/// <summary>消费估算的最小有效区间数与最小跨度。</summary>
public static class ConsumptionEstimateConstants
{
    public const int MinValidIntervals = 3;
    public static readonly TimeSpan MinSpan = TimeSpan.FromHours(24);
}

public sealed class ConsumptionEstimateService : IConsumptionEstimateService
{
    public ConsumptionEstimate Estimate(
        IReadOnlyList<TimePoint> points,
        BalanceMetricKind metricKind,
        decimal? currentAvailable,
        bool isUnlimited)
    {
        if (metricKind == BalanceMetricKind.Usage)
        {
            // 累计使用量单调增加：可估算每日使用量，但不计算“预计可用天数”。
            return EstimateUsage(points);
        }

        if (metricKind is not (BalanceMetricKind.MonetaryBalance
            or BalanceMetricKind.PlatformCredits
            or BalanceMetricKind.KeyQuota
            or BalanceMetricKind.Other))
        {
            return Unavailable(EstimateUnavailableReason.UnsupportedMetric);
        }

        if (isUnlimited)
        {
            return new ConsumptionEstimate
            {
                IsAvailable = false,
                UnavailableReason = EstimateUnavailableReason.UnsupportedMetric,
            };
        }

        if (currentAvailable is null)
        {
            return Unavailable(EstimateUnavailableReason.UnknownCurrentValue);
        }

        // 只保留数值有效且按时间升序的点。
        var valid = points
            .Where(p => p.Value is not null)
            .OrderBy(p => p.TimeUtc)
            .Select(p => new TimePoint(p.TimeUtc, p.Value!.Value))
            .ToList();

        if (valid.Count < 2)
        {
            return Unavailable(EstimateUnavailableReason.NotEnoughData);
        }

        // 相邻点：余额下降才计为有效消耗区间；忽略上涨/充值/重置/零间隔。
        var rates = new List<decimal>();
        for (int i = 1; i < valid.Count; i++)
        {
            var prev = valid[i - 1];
            var cur = valid[i];
            TimeSpan span = cur.TimeUtc - prev.TimeUtc;
            if (span <= TimeSpan.Zero)
            {
                continue;
            }

            decimal drop = prev.Value!.Value - cur.Value!.Value;
            if (drop <= 0)
            {
                continue; // 上涨、充值或重置：忽略。
            }

            decimal days = (decimal)span.TotalDays;
            if (days <= 0)
            {
                continue;
            }

            rates.Add(drop / days);
        }

        if (rates.Count < ConsumptionEstimateConstants.MinValidIntervals)
        {
            return Unavailable(EstimateUnavailableReason.NotEnoughData);
        }

        DateTimeOffset start = valid[0].TimeUtc;
        DateTimeOffset end = valid[^1].TimeUtc;
        if (end - start < ConsumptionEstimateConstants.MinSpan)
        {
            return Unavailable(EstimateUnavailableReason.TimeSpanTooShort);
        }

        decimal median = Median(rates);
        if (median <= 0)
        {
            return Unavailable(EstimateUnavailableReason.NoConsumption);
        }

        // 负值/无限天数不得显示为正常结果。
        if (currentAvailable.Value <= 0)
        {
            return new ConsumptionEstimate
            {
                IsAvailable = false,
                UnavailableReason = EstimateUnavailableReason.UnknownCurrentValue,
                DailyConsumption = median,
                ValidIntervals = rates.Count,
                DataStartUtc = start,
                DataEndUtc = end,
            };
        }

        decimal daysLeft = currentAvailable.Value / median;
        if (daysLeft is <= 0 or > 9_999_999m)
        {
            return new ConsumptionEstimate
            {
                IsAvailable = false,
                UnavailableReason = EstimateUnavailableReason.NonPositiveConsumption,
                DailyConsumption = median,
                ValidIntervals = rates.Count,
                DataStartUtc = start,
                DataEndUtc = end,
            };
        }

        return new ConsumptionEstimate
        {
            IsAvailable = true,
            DailyConsumption = median,
            EstimatedDaysLeft = daysLeft,
            ValidIntervals = rates.Count,
            DataStartUtc = start,
            DataEndUtc = end,
        };
    }

    /// <summary>累计使用量：只给每日使用量（正数=使用），不给预计天数。</summary>
    private static ConsumptionEstimate EstimateUsage(IReadOnlyList<TimePoint> points)
    {
        var valid = points
            .Where(p => p.Value is not null)
            .OrderBy(p => p.TimeUtc)
            .Select(p => new TimePoint(p.TimeUtc, p.Value!.Value))
            .ToList();

        if (valid.Count < 2)
        {
            return Unavailable(EstimateUnavailableReason.NotEnoughData);
        }

        var rates = new List<decimal>();
        for (int i = 1; i < valid.Count; i++)
        {
            var prev = valid[i - 1];
            var cur = valid[i];
            TimeSpan span = cur.TimeUtc - prev.TimeUtc;
            if (span <= TimeSpan.Zero)
            {
                continue;
            }

            decimal usage = cur.Value!.Value - prev.Value!.Value;
            if (usage < 0)
            {
                continue; // 单调递增假设被破坏：忽略下降段。
            }

            decimal days = (decimal)span.TotalDays;
            if (days <= 0)
            {
                continue;
            }

            rates.Add(usage / days);
        }

        if (rates.Count < ConsumptionEstimateConstants.MinValidIntervals)
        {
            return Unavailable(EstimateUnavailableReason.NotEnoughData);
        }

        DateTimeOffset start = valid[0].TimeUtc;
        DateTimeOffset end = valid[^1].TimeUtc;
        if (end - start < ConsumptionEstimateConstants.MinSpan)
        {
            return Unavailable(EstimateUnavailableReason.TimeSpanTooShort);
        }

        decimal median = Median(rates);
        if (median <= 0)
        {
            return Unavailable(EstimateUnavailableReason.NoConsumption);
        }

        return new ConsumptionEstimate
        {
            IsAvailable = false,
            UnavailableReason = EstimateUnavailableReason.UnsupportedMetric,
            DailyConsumption = median,
            ValidIntervals = rates.Count,
            DataStartUtc = start,
            DataEndUtc = end,
        };
    }

    private static ConsumptionEstimate Unavailable(EstimateUnavailableReason reason) =>
        new()
        {
            IsAvailable = false,
            UnavailableReason = reason,
        };

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2m;
    }
}
