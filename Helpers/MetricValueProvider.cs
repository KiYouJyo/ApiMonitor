using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Helpers;

/// <summary>
/// v0.9.0：通用指标数值取值器。Decimal 取 AvailableAmount，
/// Integer 取 IntegerValue（延迟/计数），其他类型返回 null（不伪造数值）。
/// </summary>
public static class MetricValueProvider
{
    public static decimal? NumericValue(BalanceMetric metric) =>
        metric.ValueKind switch
        {
            MetricValueKind.Decimal => metric.AvailableAmount,
            MetricValueKind.Integer => metric.IntegerValue is { } value ? value : null,
            _ => null,
        };

    /// <summary>指标展示文本（状态/布尔/时间戳/数值统一）。</summary>
    public static string ValueText(BalanceMetric metric, Services.IAppStrings? strings = null)
    {
        switch (metric.ValueKind)
        {
            case MetricValueKind.Status:
                return metric.StatusValue is { } status
                    ? Services.GeospatialMetricFactory.StatusText(
                        Services.GeospatialMetricFactory.Parse(status))
                    : L10n.Get("Insights.UnknownValue");
            case MetricValueKind.Boolean:
                return metric.BooleanValue switch
                {
                    true => L10n.Get("Metric.BooleanYes"),
                    false => L10n.Get("Metric.BooleanNo"),
                    _ => L10n.Get("Insights.UnknownValue"),
                };
            case MetricValueKind.Timestamp:
                return metric.TimestampValue is { } timestamp
                    ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    : L10n.Get("Insights.UnknownValue");
            case MetricValueKind.Integer:
                return metric.IntegerValue is { } integer
                    ? integer.ToString(System.Globalization.CultureInfo.CurrentCulture)
                    : L10n.Get("Insights.UnknownValue");
            default:
                return BalanceMetricText.FormatAmount(NumericValue(metric));
        }
    }
}
