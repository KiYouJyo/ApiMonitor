using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Helpers;

/// <summary>
/// 通用指标展示文本（账户卡片、历史、紧凑窗口与通知正文共用），
/// 避免 UI 直接引用 Provider DTO。数值未知显示“未知”，绝不用 0 表示。
/// </summary>
public static class BalanceMetricText
{
    public static string UnknownText => L10n.Get("Insights.UnknownValue");

    public static string FormatAmount(decimal? value) =>
        value is null ? UnknownText : BalanceFormatter.Format(value.Value);

    /// <summary>指标的主金额（可用 → 总额 → 使用量）。</summary>
    public static decimal? MainAmount(BalanceMetric metric) =>
        metric.AvailableAmount ?? metric.TotalAmount ?? metric.UsedAmount;

    /// <summary>“剩余 Credits 4.25”“CNY 余额 8.50”这类指标值文本。</summary>
    public static string ValueText(BalanceMetric metric, decimal? amount = null)
    {
        decimal? value = amount ?? MainAmount(metric);
        if (metric.Kind == BalanceMetricKind.MonetaryBalance)
        {
            return L10n.Format("Metric.BalanceFormat", metric.Unit, FormatAmount(value));
        }

        if (metric.Kind == BalanceMetricKind.PlatformCredits)
        {
            return L10n.Format("Metric.ValueFormat", metric.DisplayName, FormatAmount(value));
        }

        if (metric.Kind == BalanceMetricKind.KeyQuota)
        {
            return L10n.Format("Metric.ValueFormat", metric.DisplayName, FormatAmount(value));
        }

        return L10n.Format("Metric.ValueFormat", metric.DisplayName, FormatAmount(value));
    }

    /// <summary>账户卡片单行文本（“CNY · 总额 … · 赠送 … · 充值 …”）。</summary>
    public static string BuildLineText(BalanceMetric metric)
    {
        if (metric.IsUnlimited)
        {
            return L10n.Format("Metric.UnlimitedFormat", metric.DisplayName);
        }

        switch (metric.Kind)
        {
            case BalanceMetricKind.MonetaryBalance:
                var monetaryParts = new List<string>
                {
                    L10n.Format("Metric.TotalFormat", FormatAmount(metric.TotalAmount ?? metric.AvailableAmount)),
                    L10n.Format("Metric.GrantedFormat", FormatAmount(metric.GrantedAmount)),
                    L10n.Format("Metric.ToppedUpFormat", FormatAmount(metric.ToppedUpAmount)),
                };
                return $"{metric.Unit} · {string.Join(" · ", monetaryParts)}";
            case BalanceMetricKind.PlatformCredits:
                var creditParts = new List<string>
                {
                    $"{metric.DisplayName} {FormatAmount(metric.AvailableAmount ?? metric.TotalAmount ?? metric.UsedAmount)}",
                };
                if (metric.TotalAmount is not null && metric.AvailableAmount is not null)
                {
                    creditParts.Add(L10n.Format("Metric.CumulativeTopUpFormat", FormatAmount(metric.TotalAmount)));
                }

                if (metric.UsedAmount is not null && metric.AvailableAmount is not null)
                {
                    creditParts.Add(L10n.Format("Metric.CumulativeUsedFormat", FormatAmount(metric.UsedAmount)));
                }

                return string.Join(" · ", creditParts);
            case BalanceMetricKind.KeyQuota:
                return $"{metric.DisplayName} {FormatAmount(metric.AvailableAmount)}"
                    + (metric.TotalAmount is null
                        ? string.Empty
                        : L10n.Format("Metric.LimitFormat", FormatAmount(metric.TotalAmount)))
                    + (metric.UsedAmount is null
                        ? string.Empty
                        : L10n.Format("Metric.UsedFormat", FormatAmount(metric.UsedAmount)));
            default:
                return $"{metric.DisplayName} {FormatAmount(MainAmount(metric))}";
        }
    }

    /// <summary>低余额通知正文的指标部分（“剩余 Credits 4.25，已低于阈值 10.00”）。</summary>
    public static string BuildLowBalanceValueText(BalanceMetric metric, decimal threshold) =>
        L10n.Format("Metric.BelowThresholdFormat", ValueText(metric), BalanceFormatter.Format(threshold));

    /// <summary>恢复通知正文的指标部分（“CNY 余额已恢复至 30.00”）。</summary>
    public static string BuildRecoveryValueText(BalanceMetric metric)
    {
        string amount = FormatAmount(MainAmount(metric));
        return metric.Kind == BalanceMetricKind.MonetaryBalance
            ? L10n.Format("Metric.RecoveredFormat", metric.Unit, amount)
            : L10n.Format("Metric.RecoveredGenericFormat", metric.DisplayName, amount);
    }
}
