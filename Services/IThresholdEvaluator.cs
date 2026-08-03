using ApiMonitor.Models;

namespace ApiMonitor.Services;

public static class ThresholdEvaluator
{
    /// <summary>
    /// 以指标的主金额（可用余额 → 总额 → 使用量）判断阈值状态：
    /// 小于阈值视为低余额，等于或大于为正常；数值未知为未知；
    /// 规则不存在/未启用/该指标不支持阈值为“未启用提醒”；
    /// 无限额度视为正常，绝不因无限额度误触发低余额提醒。
    /// </summary>
    public static ThresholdStatus Evaluate(BalanceMetric? latest, BalanceThresholdRule? rule)
    {
        if (latest is null)
        {
            return ThresholdStatus.Unknown;
        }

        if (rule is null || !rule.IsEnabled)
        {
            return ThresholdStatus.Disabled;
        }

        if (!latest.IsThresholdSupported)
        {
            return ThresholdStatus.Disabled;
        }

        if (latest.IsUnlimited)
        {
            return ThresholdStatus.Normal;
        }

        decimal? amount = latest.AvailableAmount ?? latest.TotalAmount;
        if (amount is null)
        {
            return ThresholdStatus.Unknown;
        }

        return amount.Value < rule.ThresholdAmount
            ? ThresholdStatus.BelowThreshold
            : ThresholdStatus.Normal;
    }
}
