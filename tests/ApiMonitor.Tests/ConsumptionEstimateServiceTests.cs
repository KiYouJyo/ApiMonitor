using ApiMonitor.Models;
using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class ConsumptionEstimateServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimePoint P(int index, decimal? value, double daysFromStart = 0) =>
        new(T0.AddDays(daysFromStart), value);

    private static readonly IConsumptionEstimateService Service = new ConsumptionEstimateService();

    [Fact]
    public void SufficientDecliningIntervals_EstimatesMedianDailyConsumption()
    {
        // 三个连续下降区间：10/1、10/1、10/1 → 中位数 10/天。
        var points = new[]
        {
            P(0, 100m, 0),
            P(1, 90m, 1),
            P(2, 80m, 2),
            P(3, 70m, 3),
        };

        var result = Service.Estimate(points, BalanceMetricKind.MonetaryBalance, currentAvailable: 70m, isUnlimited: false);

        Assert.True(result.IsAvailable);
        Assert.Equal(10m, result.DailyConsumption);
        Assert.Equal(7m, result.EstimatedDaysLeft);
        Assert.Equal(3, result.ValidIntervals);
    }

    [Fact]
    public void MedianReducesSingleOutlierImpact()
    {
        // 区间率：10、100、10 → 中位数 10（100 是异常值）。
        var points = new[]
        {
            P(0, 100m, 0),
            P(1, 90m, 1),
            P(2, -10m, 2),
            P(3, -20m, 3),
        };

        var result = Service.Estimate(points, BalanceMetricKind.PlatformCredits, currentAvailable: -20m, isUnlimited: false);

        // 当前值负：不可估算（未知/异常），但仍报告中位数。
        Assert.False(result.IsAvailable);
        Assert.Equal(EstimateUnavailableReason.UnknownCurrentValue, result.UnavailableReason);
        Assert.Equal(10m, result.DailyConsumption);
    }

    [Fact]
    public void TopUpAndResetIntervals_AreIgnored()
    {
        // 充值（上涨）+ 正常下降 + 充值：只计下降区间。
        var points = new[]
        {
            P(0, 50m, 0),
            P(1, 100m, 1),   // 上涨：忽略
            P(2, 90m, 2),    // 下降 10/天
            P(3, 80m, 3),    // 下降 10/天
        };

        var result = Service.Estimate(points, BalanceMetricKind.MonetaryBalance, currentAvailable: 80m, isUnlimited: false);

        // 只有 1 个有效下降区间？不对：50→100 忽略，(100,90),(90,80) 共 2 个下降区间。
        // 实际：50→100 忽略；100→90 有效；90→80 有效 → 2 个区间 < 3。
        Assert.False(result.IsAvailable);
        Assert.Equal(EstimateUnavailableReason.NotEnoughData, result.UnavailableReason);
    }

    [Fact]
    public void ZeroTimeSpan_IsIgnored()
    {
        var points = new[]
        {
            P(0, 100m, 0),
            P(1, 90m, 0),    // 同时间：零间隔，忽略
            P(2, 80m, 1),
            P(3, 70m, 2),
            P(4, 60m, 3),
        };

        var result = Service.Estimate(points, BalanceMetricKind.MonetaryBalance, currentAvailable: 60m, isUnlimited: false);

        // 有效区间：(100→80)=20/1天、(80→70)=10/天、(70→60)=10/天 → 中位数 10。
        Assert.True(result.IsAvailable);
        Assert.Equal(10m, result.DailyConsumption);
    }

    [Fact]
    public void UnknownValues_AreSkipped_NotTreatedAsZero()
    {
        var points = new[]
        {
            P(0, 100m, 0),
            P(1, null, 1),   // 未知：跳过
            P(2, 80m, 2),
            P(3, 70m, 3),
            P(4, 60m, 4),
        };

        var result = Service.Estimate(points, BalanceMetricKind.MonetaryBalance, currentAvailable: 60m, isUnlimited: false);

        // 有效点：100@0、80@2、70@3、60@4 → 区间 (100→80)/2天=10/天、(80→70)=10、(70→60)=10。
        Assert.True(result.IsAvailable);
        Assert.Equal(10m, result.DailyConsumption);
    }

    [Fact]
    public void FewerThanThreeIntervals_IsNotEnoughData()
    {
        var points = new[]
        {
            P(0, 100m, 0),
            P(1, 90m, 1),
            P(2, 80m, 2),
        };

        var result = Service.Estimate(points, BalanceMetricKind.MonetaryBalance, currentAvailable: 80m, isUnlimited: false);

        Assert.False(result.IsAvailable);
        Assert.Equal(EstimateUnavailableReason.NotEnoughData, result.UnavailableReason);
    }

    [Fact]
    public void SpanShorterThan24Hours_IsTimeSpanTooShort()
    {
        var points = new[]
        {
            P(0, 100m, 0),
            P(1, 90m, 0.2),
            P(2, 80m, 0.4),
            P(3, 70m, 0.6),
        };

        var result = Service.Estimate(points, BalanceMetricKind.MonetaryBalance, currentAvailable: 70m, isUnlimited: false);

        Assert.False(result.IsAvailable);
        Assert.Equal(EstimateUnavailableReason.TimeSpanTooShort, result.UnavailableReason);
    }

    [Fact]
    public void UnknownCurrentValue_IsReported()
    {
        var points = new[]
        {
            P(0, 100m, 0),
            P(1, 90m, 1),
            P(2, 80m, 2),
            P(3, 70m, 3),
        };

        var result = Service.Estimate(points, BalanceMetricKind.MonetaryBalance, currentAvailable: null, isUnlimited: false);

        Assert.False(result.IsAvailable);
        Assert.Equal(EstimateUnavailableReason.UnknownCurrentValue, result.UnavailableReason);
    }

    [Fact]
    public void Unlimited_IsNotPredictable()
    {
        var points = new[]
        {
            P(0, 100m, 0),
            P(1, 90m, 1),
            P(2, 80m, 2),
            P(3, 70m, 3),
        };

        var result = Service.Estimate(points, BalanceMetricKind.KeyQuota, currentAvailable: 70m, isUnlimited: true);

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void UsageMetric_ReportsConsumptionButNoDaysLeft()
    {
        var points = new[]
        {
            P(0, 0m, 0),
            P(1, 10m, 1),
            P(2, 20m, 2),
            P(3, 30m, 3),
        };

        var result = Service.Estimate(points, BalanceMetricKind.Usage, currentAvailable: 30m, isUnlimited: false);

        // Usage 只给每日使用量（10/天），不给预计天数。
        Assert.False(result.IsAvailable);
        Assert.Equal(EstimateUnavailableReason.UnsupportedMetric, result.UnavailableReason);
        Assert.Equal(10m, result.DailyConsumption);
        Assert.Null(result.EstimatedDaysLeft);
    }

    [Fact]
    public void NoConsumptionAtAll_IsNoConsumption()
    {
        var points = new[]
        {
            P(0, 100m, 0),
            P(1, 100m, 1),
            P(2, 100m, 2),
            P(3, 100m, 3),
        };

        var result = Service.Estimate(points, BalanceMetricKind.MonetaryBalance, currentAvailable: 100m, isUnlimited: false);

        Assert.False(result.IsAvailable);
        Assert.Equal(EstimateUnavailableReason.NotEnoughData, result.UnavailableReason);
    }

    [Fact]
    public void CurrentValueZero_DoesNotShowNormalDays()
    {
        var points = new[]
        {
            P(0, 100m, 0),
            P(1, 50m, 1),
            P(2, 25m, 2),
            P(3, 0m, 3),
        };

        var result = Service.Estimate(points, BalanceMetricKind.MonetaryBalance, currentAvailable: 0m, isUnlimited: false);

        Assert.False(result.IsAvailable);
        Assert.Null(result.EstimatedDaysLeft);
    }
}
