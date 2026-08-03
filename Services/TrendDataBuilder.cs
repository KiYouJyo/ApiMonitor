using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 趋势图数据点（数值未知时为 null，不绘制虚假连续线）。
/// </summary>
public readonly record struct TrendPoint(DateTimeOffset TimeUtc, decimal? Value);

/// <summary>时间范围选项（数据洞察页）。</summary>
public enum InsightsTimeRange
{
    Days7,
    Days30,
    Days90,
    All,
}

/// <summary>
/// 趋势数据纯逻辑：从历史条目中提取指定账户/指标的时间序列，
/// 按时间范围筛选，并在数据过多时按时间分桶抽样。
/// 抽样保留首点、末点与区间极值；原始历史记录绝不因抽样而修改。
/// </summary>
public interface ITrendDataBuilder
{
    /// <summary>
    /// 从历史（任意顺序）构建趋势点序列。
    /// metricId 为 null 时返回空序列；未知值以 null 表示（不当作 0）。
    /// 返回点按时间升序。不连接跨越缺失数据的虚假连续线（UI 绘制时处理）。
    /// </summary>
    IReadOnlyList<TrendPoint> Build(
        IReadOnlyList<BalanceHistoryEntry> history,
        string metricId,
        InsightsTimeRange range,
        DateTimeOffset nowUtc,
        int maxPoints = 500);
}

public sealed class TrendDataBuilder : ITrendDataBuilder
{
    public IReadOnlyList<TrendPoint> Build(
        IReadOnlyList<BalanceHistoryEntry> history,
        string metricId,
        InsightsTimeRange range,
        DateTimeOffset nowUtc,
        int maxPoints = 500)
    {
        if (string.IsNullOrWhiteSpace(metricId) || history is null || history.Count == 0)
        {
            return Array.Empty<TrendPoint>();
        }

        DateTimeOffset? fromUtc = range switch
        {
            InsightsTimeRange.Days7 => nowUtc.AddDays(-7),
            InsightsTimeRange.Days30 => nowUtc.AddDays(-30),
            InsightsTimeRange.Days90 => nowUtc.AddDays(-90),
            _ => null,
        };

        var points = new List<TrendPoint>(history.Count);
        foreach (var entry in history)
        {
            if (!entry.IsAvailable)
            {
                continue;
            }

            if (fromUtc is { } from && entry.SucceededAtUtc < from)
            {
                continue;
            }

            foreach (var metric in entry.Metrics)
            {
                if (string.Equals(metric.MetricId, metricId, StringComparison.OrdinalIgnoreCase))
                {
                    points.Add(new TrendPoint(entry.SucceededAtUtc, metric.AvailableAmount));
                    break;
                }
            }
        }

        points.Sort(static (a, b) => a.TimeUtc.CompareTo(b.TimeUtc));

        if (points.Count <= maxPoints)
        {
            return points;
        }

        return Sample(points, maxPoints);
    }

    /// <summary>
    /// 按时间分桶抽样：均分到 maxPoints 个桶，每桶输出一个代表点。
    /// 首桶 = 全局首点，末桶 = 全局末点；中间桶优先输出桶内
    /// 偏离桶均值更大的极值点（保留区间极值）。输出数量 ≤ maxPoints。
    /// 原始历史绝不因抽样而修改。
    /// </summary>
    public static IReadOnlyList<TrendPoint> Sample(IReadOnlyList<TrendPoint> points, int maxPoints)
    {
        if (points.Count <= maxPoints || maxPoints < 3)
        {
            return points;
        }

        var result = new List<TrendPoint>(maxPoints);
        for (int bucket = 0; bucket < maxPoints; bucket++)
        {
            int start = bucket * points.Count / maxPoints;
            int end = Math.Max(start + 1, (bucket + 1) * points.Count / maxPoints);
            var bucketPoints = points.Skip(start).Take(end - start).ToList();
            if (bucketPoints.Count == 0)
            {
                continue;
            }

            if (bucket == 0)
            {
                // 首桶：保留全局首点。
                result.Add(points[0]);
                continue;
            }

            if (bucket == maxPoints - 1)
            {
                // 末桶：保留全局末点。
                result.Add(points[^1]);
                continue;
            }

            // 中间桶：优先输出桶内极值（偏离均值更大的点）；数值未知的点不参与。
            var valid = bucketPoints.Where(p => p.Value is not null).ToList();
            if (valid.Count == 0)
            {
                result.Add(bucketPoints[0]);
                continue;
            }

            decimal mean = valid.Average(p => p.Value!.Value);
            var representative = valid
                .OrderByDescending(p => Math.Abs(p.Value!.Value - mean))
                .First();
            result.Add(representative);
        }

        return result;
    }
}
