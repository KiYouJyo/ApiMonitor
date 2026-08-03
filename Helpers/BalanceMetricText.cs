using ApiMonitor.Models;

namespace ApiMonitor.Helpers;

/// <summary>
/// 通用指标展示文本（账户卡片、历史、紧凑窗口与通知正文共用），
/// 避免 UI 直接引用 Provider DTO。数值未知显示“未知”，绝不用 0 表示。
/// </summary>
public static class BalanceMetricText
{
    public const string UnknownText = "未知";

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
            return $"{metric.Unit} 余额 {FormatAmount(value)}";
        }

        if (metric.Kind == BalanceMetricKind.PlatformCredits)
        {
            return $"{metric.DisplayName} {FormatAmount(value)}";
        }

        if (metric.Kind == BalanceMetricKind.KeyQuota)
        {
            return $"{metric.DisplayName} {FormatAmount(value)}";
        }

        return $"{metric.DisplayName} {FormatAmount(value)}";
    }

    /// <summary>账户卡片单行文本（“CNY · 总额 … · 赠送 … · 充值 …”）。</summary>
    public static string BuildLineText(BalanceMetric metric)
    {
        if (metric.IsUnlimited)
        {
            return $"{metric.DisplayName}：无限额度";
        }

        switch (metric.Kind)
        {
            case BalanceMetricKind.MonetaryBalance:
                var monetaryParts = new List<string>
                {
                    $"总额 {FormatAmount(metric.TotalAmount ?? metric.AvailableAmount)}",
                    $"赠送 {FormatAmount(metric.GrantedAmount)}",
                    $"充值 {FormatAmount(metric.ToppedUpAmount)}",
                };
                return $"{metric.Unit} · {string.Join(" · ", monetaryParts)}";
            case BalanceMetricKind.PlatformCredits:
                var creditParts = new List<string>
                {
                    $"{metric.DisplayName} {FormatAmount(metric.AvailableAmount ?? metric.TotalAmount ?? metric.UsedAmount)}",
                };
                if (metric.TotalAmount is not null && metric.AvailableAmount is not null)
                {
                    creditParts.Add($"累计充值 {FormatAmount(metric.TotalAmount)}");
                }

                if (metric.UsedAmount is not null && metric.AvailableAmount is not null)
                {
                    creditParts.Add($"累计使用 {FormatAmount(metric.UsedAmount)}");
                }

                return string.Join(" · ", creditParts);
            case BalanceMetricKind.KeyQuota:
                return $"{metric.DisplayName} {FormatAmount(metric.AvailableAmount)}"
                    + (metric.TotalAmount is null
                        ? string.Empty
                        : $" / 上限 {FormatAmount(metric.TotalAmount)}")
                    + (metric.UsedAmount is null
                        ? string.Empty
                        : $" · 已用 {FormatAmount(metric.UsedAmount)}");
            default:
                return $"{metric.DisplayName} {FormatAmount(MainAmount(metric))}";
        }
    }

    /// <summary>低余额通知正文的指标部分（“剩余 Credits 4.25，已低于阈值 10.00”）。</summary>
    public static string BuildLowBalanceValueText(BalanceMetric metric, decimal threshold) =>
        $"{ValueText(metric)}，已低于阈值 {BalanceFormatter.Format(threshold)}";

    /// <summary>恢复通知正文的指标部分（“CNY 余额已恢复至 30.00”）。</summary>
    public static string BuildRecoveryValueText(BalanceMetric metric)
    {
        string amount = FormatAmount(MainAmount(metric));
        return metric.Kind == BalanceMetricKind.MonetaryBalance
            ? $"{metric.Unit} 余额已恢复至 {amount}"
            : $"{metric.DisplayName} 已恢复至 {amount}";
    }
}
