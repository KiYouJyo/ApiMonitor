using System.Globalization;
using ApiMonitor.Helpers;
using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiMonitor.ViewModels;

/// <summary>编辑对话框中单个指标的阈值编辑项（含当前余额与实时状态）。</summary>
public sealed partial class ThresholdEditorItem : ObservableObject
{
    public string MetricId { get; }

    public string DisplayName { get; }

    public string Unit { get; }

    [ObservableProperty]
    private decimal? _currentAmount;

    public string CurrentBalanceText => BalanceMetricText.FormatAmount(CurrentAmount);

    public string CurrentBalanceLine => L10n.Format("Threshold.CurrentBalanceFormat", CurrentBalanceText);

    public BalanceThresholdRule? OriginalRule { get; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _thresholdText = string.Empty;

    [ObservableProperty]
    private string _statusText = L10n.Get("Card.AlertsDisabled");

    public string StatusLine => L10n.Format("Threshold.StatusFormat", StatusText);

    public ThresholdEditorItem(BalanceMetric metric, BalanceThresholdRule? rule)
    {
        MetricId = metric.MetricId;
        DisplayName = metric.DisplayName;
        Unit = metric.Unit;
        _currentAmount = BalanceMetricText.MainAmount(metric);
        OriginalRule = rule;
        _isEnabled = rule?.IsEnabled ?? false;
        _thresholdText = rule is not null
            ? rule.ThresholdAmount.ToString("0.##", CultureInfo.CurrentCulture)
            : string.Empty;
        Recompute();
    }

    partial void OnCurrentAmountChanged(decimal? value)
    {
        OnPropertyChanged(nameof(CurrentBalanceText));
        OnPropertyChanged(nameof(CurrentBalanceLine));
        Recompute();
    }

    partial void OnIsEnabledChanged(bool value) => Recompute();

    partial void OnThresholdTextChanged(string value) => Recompute();

    partial void OnStatusTextChanged(string value) =>
        OnPropertyChanged(nameof(StatusLine));

    /// <summary>
    /// 解析阈值金额：优先当前区域设置，其次固定小数点区域；
    /// decimal 不存在 NaN/无限值，解析成功且不小于 0 即合法。
    /// </summary>
    public bool TryParseAmount(out decimal amount)
    {
        if (!string.IsNullOrWhiteSpace(ThresholdText)
            && (decimal.TryParse(ThresholdText, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
                || decimal.TryParse(ThresholdText, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)))
        {
            return amount >= 0m;
        }

        amount = 0m;
        return false;
    }

    /// <summary>构建规则；无有效金额且无历史规则时返回 null（不创建规则）。</summary>
    public BalanceThresholdRule? BuildRule(DateTimeOffset nowUtc)
    {
        if (TryParseAmount(out var amount))
        {
            return new BalanceThresholdRule
            {
                MetricId = MetricId,
                DisplayName = DisplayName,
                Unit = Unit,
                IsEnabled = IsEnabled,
                ThresholdAmount = amount,
                CreatedAtUtc = OriginalRule?.CreatedAtUtc ?? nowUtc,
                UpdatedAtUtc = nowUtc,
            };
        }

        if (OriginalRule is not null)
        {
            // 保留原有配置但关闭提醒，避免编辑时误删。
            return new BalanceThresholdRule
            {
                MetricId = MetricId,
                DisplayName = DisplayName,
                Unit = Unit,
                IsEnabled = false,
                ThresholdAmount = OriginalRule.ThresholdAmount,
                CreatedAtUtc = OriginalRule.CreatedAtUtc,
                UpdatedAtUtc = nowUtc,
            };
        }

        return null;
    }

    private void Recompute()
    {
        if (!IsEnabled)
        {
            StatusText = L10n.Get("Card.AlertsDisabled");
            return;
        }

        if (!TryParseAmount(out var amount))
        {
            StatusText = L10n.Get("Threshold.InvalidAmount");
            return;
        }

        var metric = new BalanceMetric
        {
            MetricId = MetricId,
            DisplayName = DisplayName,
            Unit = Unit,
            Kind = BalanceMetricKind.Other,
            AvailableAmount = CurrentAmount,
            TotalAmount = CurrentAmount,
            IsThresholdSupported = true,
        };
        var rule = new BalanceThresholdRule
        {
            MetricId = MetricId,
            DisplayName = DisplayName,
            Unit = Unit,
            IsEnabled = true,
            ThresholdAmount = amount,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        StatusText = ThresholdEvaluator.Evaluate(metric, rule) switch
        {
            ThresholdStatus.BelowThreshold => L10n.Format("Threshold.BelowFormat", BalanceFormatter.Format(amount)),
            ThresholdStatus.Normal => L10n.Get("Home.StatusNormal"),
            _ => L10n.Get("Card.AlertsDisabled"),
        };
    }
}
