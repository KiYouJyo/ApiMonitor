using ApiMonitor.Models;

namespace ApiMonitor.Services;

public static class ThresholdEvaluator
{
    /// <summary>
    /// 以 TotalBalance 判断阈值状态：小于阈值视为低余额，等于或大于为正常；
    /// 无余额数据为未知；规则不存在或未启用为“未启用提醒”。
    /// </summary>
    public static ThresholdStatus Evaluate(BalanceAmount? latest, BalanceThresholdRule? rule)
    {
        if (latest is null)
        {
            return ThresholdStatus.Unknown;
        }

        if (rule is null || !rule.IsEnabled)
        {
            return ThresholdStatus.Disabled;
        }

        return latest.TotalBalance < rule.ThresholdAmount
            ? ThresholdStatus.BelowThreshold
            : ThresholdStatus.Normal;
    }
}
