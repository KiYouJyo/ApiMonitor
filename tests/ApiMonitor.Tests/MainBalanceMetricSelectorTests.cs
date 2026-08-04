using ApiMonitor.Helpers;
using ApiMonitor.Models;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 悬浮窗主额度选择规则测试（v0.7.0）：DeepSeek 用可用总余额、
/// OpenRouter 普通 Key 用剩余额度、Management Key 用剩余 Credits、
/// 无剩余值时选最合理可用指标、绝不把累计使用量当作主数字。
/// </summary>
public sealed class MainBalanceMetricSelectorTests
{
    private static BalanceMetric Metric(
        string metricId,
        BalanceMetricKind kind,
        decimal? available = null,
        decimal? total = null,
        decimal? used = null) =>
        new()
        {
            MetricId = metricId,
            DisplayName = metricId,
            Unit = "credits",
            Kind = kind,
            AvailableAmount = available,
            TotalAmount = total,
            UsedAmount = used,
        };

    [Fact]
    public void DeepSeek_PrefersAvailableTotalBalance()
    {
        var selected = MainBalanceMetricSelector.Select(new[]
        {
            Metric("deepseek:CNY:total", BalanceMetricKind.MonetaryBalance, available: 88m, total: 88m),
            Metric("deepseek:USD:total", BalanceMetricKind.MonetaryBalance, available: 10m, total: 10m),
        });

        Assert.NotNull(selected);
        Assert.Equal("deepseek:CNY:total", selected!.MetricId);
        Assert.Equal(88m, MainBalanceMetricSelector.MainAmount(selected));
    }

    [Fact]
    public void OpenRouterApiKey_PrefersQuotaRemaining_OverUsage()
    {
        var selected = MainBalanceMetricSelector.Select(new[]
        {
            Metric("openrouter:key:quota-remaining", BalanceMetricKind.KeyQuota, available: 42m, total: 100m),
            Metric("openrouter:key:quota-limit", BalanceMetricKind.KeyQuota, total: 100m),
            Metric("openrouter:key:usage-total", BalanceMetricKind.Usage, used: 58m),
        });

        Assert.NotNull(selected);
        Assert.Equal("openrouter:key:quota-remaining", selected!.MetricId);
        Assert.Equal(42m, MainBalanceMetricSelector.MainAmount(selected));
    }

    [Fact]
    public void OpenRouterManagementKey_PrefersRemainingCredits()
    {
        var selected = MainBalanceMetricSelector.Select(new[]
        {
            Metric("openrouter:credits:total", BalanceMetricKind.PlatformCredits, total: 100m),
            Metric("openrouter:credits:remaining", BalanceMetricKind.PlatformCredits, available: 8.5m, total: 100m, used: 91.5m),
            Metric("openrouter:credits:usage", BalanceMetricKind.Usage, used: 91.5m),
        });

        Assert.NotNull(selected);
        Assert.Equal("openrouter:credits:remaining", selected!.MetricId);
        Assert.Equal(8.5m, MainBalanceMetricSelector.MainAmount(selected));
    }

    [Fact]
    public void UsageMetrics_AreNeverSelectedAsMainNumber()
    {
        var selected = MainBalanceMetricSelector.Select(new[]
        {
            Metric("openrouter:key:usage-total", BalanceMetricKind.Usage, used: 999m),
            Metric("openrouter:key:usage-monthly", BalanceMetricKind.Usage, used: 10m),
        });

        Assert.Null(selected);
    }

    [Fact]
    public void NoRemainingValue_FallsBackToMostReasonableAvailableMetric()
    {
        // 无剩余值时（如未设限额的 Key），选带总额的上限指标作为最合理可用指标。
        var selected = MainBalanceMetricSelector.Select(new[]
        {
            Metric("openrouter:key:quota-remaining", BalanceMetricKind.KeyQuota, total: 100m),
            Metric("openrouter:key:quota-limit", BalanceMetricKind.KeyQuota, total: 100m),
        });

        Assert.NotNull(selected);
        Assert.Equal(100m, MainBalanceMetricSelector.MainAmount(selected));
    }

    [Fact]
    public void EmptyOrNullMetrics_ReturnsNull()
    {
        Assert.Null(MainBalanceMetricSelector.Select(Array.Empty<BalanceMetric>()));
        Assert.Null(MainBalanceMetricSelector.Select(null!));
    }

    [Fact]
    public void Selection_IsDeterministic_ByMetricId()
    {
        var a = MainBalanceMetricSelector.Select(new[]
        {
            Metric("x:z", BalanceMetricKind.Other, available: 1m),
            Metric("x:a", BalanceMetricKind.Other, available: 1m),
        });
        var b = MainBalanceMetricSelector.Select(new[]
        {
            Metric("x:a", BalanceMetricKind.Other, available: 1m),
            Metric("x:z", BalanceMetricKind.Other, available: 1m),
        });

        Assert.Equal(a!.MetricId, b!.MetricId);
        Assert.Equal("x:a", a.MetricId);
    }
}
