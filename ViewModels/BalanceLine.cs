using ApiMonitor.Helpers;
using ApiMonitor.Models;

namespace ApiMonitor.ViewModels;

/// <summary>账户卡片中单个指标余额的展示模型（基于通用 BalanceMetric）。</summary>
public sealed class BalanceLine
{
    public string MetricId { get; }

    public string DisplayName { get; }

    public string Unit { get; }

    public string MainAmountText { get; }

    public string LineText { get; }

    public BalanceLine(BalanceMetric metric)
    {
        MetricId = metric.MetricId;
        DisplayName = metric.DisplayName;
        Unit = metric.Unit;
        MainAmountText = BalanceMetricText.FormatAmount(BalanceMetricText.MainAmount(metric));
        LineText = BalanceMetricText.BuildLineText(metric);
    }
}
