using System.Globalization;
using ApiMonitor.Helpers;
using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiMonitor.ViewModels;

/// <summary>编辑对话框中单个币种的阈值编辑项（含当前余额与实时状态）。</summary>
public sealed partial class ThresholdEditorItem : ObservableObject
{
    public string Currency { get; }

    [ObservableProperty]
    private decimal _currentTotal;

    public string CurrentBalanceText => BalanceFormatter.Format(CurrentTotal);

    public string CurrentBalanceLine => $"当前余额：{CurrentBalanceText}";

    public BalanceThresholdRule? OriginalRule { get; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _thresholdText = string.Empty;

    [ObservableProperty]
    private string _statusText = "未启用提醒";

    public string StatusLine => $"状态：{StatusText}";

    public ThresholdEditorItem(string currency, decimal currentTotal, BalanceThresholdRule? rule)
    {
        Currency = currency;
        _currentTotal = currentTotal;
        OriginalRule = rule;
        _isEnabled = rule?.IsEnabled ?? false;
        _thresholdText = rule is not null
            ? rule.ThresholdAmount.ToString("0.##", CultureInfo.CurrentCulture)
            : string.Empty;
        Recompute();
    }

    partial void OnCurrentTotalChanged(decimal value)
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
                Currency = Currency,
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
                Currency = Currency,
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
            StatusText = "未启用提醒";
            return;
        }

        if (!TryParseAmount(out var amount))
        {
            StatusText = "无效金额";
            return;
        }

        var balance = new BalanceAmount
        {
            Currency = Currency,
            TotalBalance = CurrentTotal,
            GrantedBalance = 0m,
            ToppedUpBalance = 0m,
        };
        var rule = new BalanceThresholdRule
        {
            Currency = Currency,
            IsEnabled = true,
            ThresholdAmount = amount,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        StatusText = ThresholdEvaluator.Evaluate(balance, rule) switch
        {
            ThresholdStatus.BelowThreshold => $"低于阈值 {BalanceFormatter.Format(amount)}",
            ThresholdStatus.Normal => "正常",
            _ => "未启用提醒",
        };
    }
}
