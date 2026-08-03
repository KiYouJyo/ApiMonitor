using ApiMonitor.Models;
using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class ThresholdEvaluatorTests
{
    private static BalanceMetric Metric(decimal? amount, bool thresholdSupported = true, bool isUnlimited = false) =>
        new()
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            Kind = BalanceMetricKind.MonetaryBalance,
            AvailableAmount = amount,
            TotalAmount = amount,
            IsThresholdSupported = thresholdSupported,
            IsUnlimited = isUnlimited,
        };

    private static BalanceThresholdRule Rule(decimal amount, bool enabled = true) =>
        new()
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            IsEnabled = enabled,
            ThresholdAmount = amount,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void BelowThreshold_IsLowBalance() =>
        Assert.Equal(ThresholdStatus.BelowThreshold, ThresholdEvaluator.Evaluate(Metric(10m), Rule(20m)));

    [Fact]
    public void EqualThreshold_IsNormal() =>
        Assert.Equal(ThresholdStatus.Normal, ThresholdEvaluator.Evaluate(Metric(20m), Rule(20m)));

    [Fact]
    public void AboveThreshold_IsNormal() =>
        Assert.Equal(ThresholdStatus.Normal, ThresholdEvaluator.Evaluate(Metric(30m), Rule(20m)));

    [Fact]
    public void DisabledRule_IsNotAlerted() =>
        Assert.Equal(ThresholdStatus.Disabled, ThresholdEvaluator.Evaluate(Metric(10m), Rule(20m, enabled: false)));

    [Fact]
    public void MissingRule_IsNotAlerted() =>
        Assert.Equal(ThresholdStatus.Disabled, ThresholdEvaluator.Evaluate(Metric(10m), null));

    [Fact]
    public void NoBalanceData_IsUnknown() =>
        Assert.Equal(ThresholdStatus.Unknown, ThresholdEvaluator.Evaluate(null, Rule(20m)));

    [Fact]
    public void UnknownAmount_IsUnknownEvenWithEnabledRule() =>
        Assert.Equal(ThresholdStatus.Unknown, ThresholdEvaluator.Evaluate(Metric(null), Rule(20m)));

    [Fact]
    public void UnlimitedMetric_NeverAlerts() =>
        Assert.Equal(
            ThresholdStatus.Normal,
            ThresholdEvaluator.Evaluate(Metric(null, isUnlimited: true), Rule(20m)));

    [Fact]
    public void NonThresholdMetric_IsDisabled() =>
        Assert.Equal(
            ThresholdStatus.Disabled,
            ThresholdEvaluator.Evaluate(Metric(10m, thresholdSupported: false), Rule(20m)));

    [Fact]
    public void MultiMetric_JudgedIndependently()
    {
        var cny = new BalanceMetric
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            Kind = BalanceMetricKind.MonetaryBalance,
            AvailableAmount = 5m,
            TotalAmount = 5m,
            IsThresholdSupported = true,
        };
        var usd = new BalanceMetric
        {
            MetricId = "deepseek:USD:total",
            DisplayName = "USD 总余额",
            Unit = "USD",
            Kind = BalanceMetricKind.MonetaryBalance,
            AvailableAmount = 150m,
            TotalAmount = 150m,
            IsThresholdSupported = true,
        };
        var cnyRule = new BalanceThresholdRule
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            IsEnabled = true,
            ThresholdAmount = 10m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var usdRule = new BalanceThresholdRule
        {
            MetricId = "deepseek:USD:total",
            DisplayName = "USD 总余额",
            Unit = "USD",
            IsEnabled = true,
            ThresholdAmount = 100m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        Assert.Equal(ThresholdStatus.BelowThreshold, ThresholdEvaluator.Evaluate(cny, cnyRule));
        Assert.Equal(ThresholdStatus.Normal, ThresholdEvaluator.Evaluate(usd, usdRule));
    }
}
