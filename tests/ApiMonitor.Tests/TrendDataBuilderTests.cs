using ApiMonitor.Models;
using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class TrendDataBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static BalanceHistoryEntry Entry(
        int index,
        double daysFromStart,
        string metricId = "deepseek:CNY:total",
        decimal? value = 100m,
        bool available = true) =>
        new()
        {
            Id = $"id-{index:D5}",
            AccountId = "acct-1",
            ProviderId = "deepseek",
            SucceededAtUtc = T0.AddDays(daysFromStart),
            Source = BalanceQuerySource.Manual,
            IsAvailable = available,
            Metrics = new[]
            {
                new BalanceMetric
                {
                    MetricId = metricId,
                    DisplayName = "CNY 总余额",
                    Unit = "CNY",
                    Kind = BalanceMetricKind.MonetaryBalance,
                    AvailableAmount = value,
                },
            },
        };

    private static readonly ITrendDataBuilder Builder = new TrendDataBuilder();

    [Fact]
    public void Build_FiltersByMetricId()
    {
        var history = new[]
        {
            Entry(0, 0, metricId: "deepseek:CNY:total"),
            Entry(1, 1, metricId: "openrouter:credits:remaining"),
        };

        var points = Builder.Build(history, "deepseek:CNY:total", InsightsTimeRange.All, T0.AddDays(3));

        var single = Assert.Single(points);
        Assert.Equal(100m, single.Value);
    }

    [Fact]
    public void Build_AppliesTimeRange()
    {
        var history = new[]
        {
            Entry(0, -40, value: 100m),  // 40 天前
            Entry(1, -20, value: 90m),   // 20 天前
            Entry(2, -5, value: 80m),    // 5 天前
        };

        var points = Builder.Build(history, "deepseek:CNY:total", InsightsTimeRange.Days30, T0);

        Assert.Equal(2, points.Count);
        Assert.All(points, p => Assert.NotNull(p.Value));
    }

    [Fact]
    public void Build_SortsAscending()
    {
        var history = new[]
        {
            Entry(0, 5, value: 80m),
            Entry(1, 0, value: 100m),
            Entry(2, 2, value: 90m),
        };

        var points = Builder.Build(history, "deepseek:CNY:total", InsightsTimeRange.All, T0.AddDays(6));

        Assert.Equal(3, points.Count);
        Assert.Equal(100m, points[0].Value);
        Assert.Equal(90m, points[1].Value);
        Assert.Equal(80m, points[2].Value);
    }

    [Fact]
    public void Build_UnknownValue_IsNull_NotZero()
    {
        var history = new[]
        {
            Entry(0, 0, value: null),
            Entry(1, 1, value: 90m),
        };

        var points = Builder.Build(history, "deepseek:CNY:total", InsightsTimeRange.All, T0.AddDays(2));

        Assert.Equal(2, points.Count);
        Assert.Null(points[0].Value);
        Assert.Equal(90m, points[1].Value);
    }

    [Fact]
    public void Build_UnavailableEntries_AreExcluded()
    {
        var history = new[]
        {
            Entry(0, 0, value: 100m, available: false),
            Entry(1, 1, value: 90m, available: true),
        };

        var points = Builder.Build(history, "deepseek:CNY:total", InsightsTimeRange.All, T0.AddDays(2));

        var single = Assert.Single(points);
        Assert.Equal(90m, single.Value);
    }

    [Fact]
    public void Build_EmptyMetricId_ReturnsEmpty()
    {
        var points = Builder.Build(new[] { Entry(0, 0) }, string.Empty, InsightsTimeRange.All, T0);

        Assert.Empty(points);
    }

    [Fact]
    public void Sample_PreservesFirstAndLast()
    {
        var points = Enumerable.Range(0, 1000)
            .Select(i => new TrendPoint(T0.AddMinutes(i), (decimal)i))
            .ToList();

        var sampled = TrendDataBuilder.Sample(points, 500);

        Assert.True(sampled.Count <= 500);
        Assert.Equal(points[0], sampled[0]);
        Assert.Equal(points[^1], sampled[^1]);
    }

    [Fact]
    public void Sample_PreservesMinAndMaxValues()
    {
        var points = Enumerable.Range(0, 1000)
            .Select(i =>
            {
                decimal v = i switch
                {
                    100 => 9999m,  // 最高值
                    900 => -9999m, // 最低值
                    _ => (decimal)i,
                };
                return new TrendPoint(T0.AddMinutes(i), v);
            })
            .ToList();

        var sampled = TrendDataBuilder.Sample(points, 100);

        var min = sampled.Where(p => p.Value is not null).Min(p => p.Value!.Value);
        var max = sampled.Where(p => p.Value is not null).Max(p => p.Value!.Value);
        Assert.Equal(-9999m, min);
        Assert.Equal(9999m, max);
    }

    [Fact]
    public void Sample_UnderLimit_ReturnsAll()
    {
        var points = Enumerable.Range(0, 10)
            .Select(i => new TrendPoint(T0.AddMinutes(i), (decimal)i))
            .ToList();

        var sampled = TrendDataBuilder.Sample(points, 500);

        Assert.Equal(10, sampled.Count);
    }

    [Fact]
    public void Build_OverLimit_UsesSampling()
    {
        var history = Enumerable.Range(0, 2000)
            .Select(i => Entry(i, i * 0.01, value: 100m - i * 0.01m))
            .ToList();

        var points = Builder.Build(history, "deepseek:CNY:total", InsightsTimeRange.All, T0.AddDays(21));

        Assert.True(points.Count <= 500);
        // 原始历史不因抽样修改。
        Assert.Equal(2000, history.Count);
    }
}
