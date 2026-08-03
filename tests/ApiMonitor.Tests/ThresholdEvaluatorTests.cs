using ApiMonitor.Models;
using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class ThresholdEvaluatorTests
{
    private static BalanceAmount Balance(decimal total) =>
        new() { Currency = "CNY", TotalBalance = total, GrantedBalance = 0m, ToppedUpBalance = 0m };

    private static BalanceThresholdRule Rule(decimal amount, bool enabled = true) =>
        new()
        {
            Currency = "CNY",
            IsEnabled = enabled,
            ThresholdAmount = amount,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void BelowThreshold_IsLowBalance() =>
        Assert.Equal(ThresholdStatus.BelowThreshold, ThresholdEvaluator.Evaluate(Balance(10m), Rule(20m)));

    [Fact]
    public void EqualThreshold_IsNormal() =>
        Assert.Equal(ThresholdStatus.Normal, ThresholdEvaluator.Evaluate(Balance(20m), Rule(20m)));

    [Fact]
    public void AboveThreshold_IsNormal() =>
        Assert.Equal(ThresholdStatus.Normal, ThresholdEvaluator.Evaluate(Balance(30m), Rule(20m)));

    [Fact]
    public void DisabledRule_IsNotAlerted() =>
        Assert.Equal(ThresholdStatus.Disabled, ThresholdEvaluator.Evaluate(Balance(10m), Rule(20m, enabled: false)));

    [Fact]
    public void MissingRule_IsNotAlerted() =>
        Assert.Equal(ThresholdStatus.Disabled, ThresholdEvaluator.Evaluate(Balance(10m), null));

    [Fact]
    public void NoBalanceData_IsUnknown() =>
        Assert.Equal(ThresholdStatus.Unknown, ThresholdEvaluator.Evaluate(null, Rule(20m)));

    [Fact]
    public void MultiCurrency_JudgedIndependently()
    {
        var cny = new BalanceAmount { Currency = "CNY", TotalBalance = 5m, GrantedBalance = 0m, ToppedUpBalance = 5m };
        var usd = new BalanceAmount { Currency = "USD", TotalBalance = 150m, GrantedBalance = 0m, ToppedUpBalance = 150m };
        var cnyRule = new BalanceThresholdRule { Currency = "CNY", IsEnabled = true, ThresholdAmount = 10m, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        var usdRule = new BalanceThresholdRule { Currency = "USD", IsEnabled = true, ThresholdAmount = 100m, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };

        Assert.Equal(ThresholdStatus.BelowThreshold, ThresholdEvaluator.Evaluate(cny, cnyRule));
        Assert.Equal(ThresholdStatus.Normal, ThresholdEvaluator.Evaluate(usd, usdRule));
    }
}
