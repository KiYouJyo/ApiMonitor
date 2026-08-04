using ApiMonitor.Models;

namespace ApiMonitor.Helpers;

/// <summary>
/// 悬浮余额窗的“主额度数字”统一选择规则（v0.7.0，集中封装，禁止在 UI 层分散硬编码）：
///   - DeepSeek：优先显示可用总余额（货币余额指标，AvailableAmount 即总余额）；
///   - OpenRouter 普通 API Key：优先显示密钥剩余额度（KeyQuota.AvailableAmount）；
///   - OpenRouter Management Key：优先显示剩余 Credits（PlatformCredits.AvailableAmount）；
///   - 无明确“剩余可用值”时选择最合理的主要可用指标（Available → Total）；
///   - 累计/周期使用量（Usage）绝不作为主数字。
/// 同类指标按 MetricId 字典序做确定性排序，避免选择随快照顺序抖动。
/// </summary>
public static class MainBalanceMetricSelector
{
    /// <summary>从快照指标中选出主额度指标；无可用指标时返回 null。</summary>
    public static BalanceMetric? Select(IReadOnlyList<BalanceMetric> metrics)
    {
        if (metrics is null || metrics.Count == 0)
        {
            return null;
        }

        return metrics
            .Where(m => m.Kind != BalanceMetricKind.Usage)
            .OrderBy(m => KindPriority(m.Kind))
            .ThenByDescending(m => m.AvailableAmount is not null)
            .ThenByDescending(m => m.TotalAmount is not null)
            .ThenBy(m => m.MetricId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>主数字取值：优先剩余/可用值，其次总额；绝不用累计使用量。</summary>
    public static decimal? MainAmount(BalanceMetric metric) =>
        metric is null ? null : metric.AvailableAmount ?? metric.TotalAmount;

    private static int KindPriority(BalanceMetricKind kind) =>
        kind switch
        {
            BalanceMetricKind.PlatformCredits => 0,
            BalanceMetricKind.KeyQuota => 1,
            BalanceMetricKind.MonetaryBalance => 2,
            _ => 3,
        };
}
