using ApiMonitor.Models;

namespace ApiMonitor.Tests.TestHelpers;

/// <summary>测试用指标/快照/阈值构造助手（避免每个测试重复样板）。</summary>
internal static class TestMetrics
{
    public static BalanceMetric Cny(decimal total) =>
        new()
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            Kind = BalanceMetricKind.MonetaryBalance,
            AvailableAmount = total,
            TotalAmount = total,
            IsThresholdSupported = true,
        };

    public static BalanceMetric Usd(decimal total) =>
        new()
        {
            MetricId = "deepseek:USD:total",
            DisplayName = "USD 总余额",
            Unit = "USD",
            Kind = BalanceMetricKind.MonetaryBalance,
            AvailableAmount = total,
            TotalAmount = total,
            IsThresholdSupported = true,
        };

    public static BalanceMetric Credits(decimal remaining, decimal? total = null, decimal? used = null) =>
        new()
        {
            MetricId = "openrouter:credits:remaining",
            DisplayName = "剩余 Credits",
            Unit = "credits",
            Kind = BalanceMetricKind.PlatformCredits,
            AvailableAmount = remaining,
            TotalAmount = total,
            UsedAmount = used,
            IsThresholdSupported = true,
        };

    public static BalanceThresholdRule CnyRule(decimal amount, bool enabled = true) =>
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

    public static BalanceThresholdRule Rule(
        string metricId,
        decimal amount,
        bool enabled = true,
        string displayName = "",
        string unit = "") =>
        new()
        {
            MetricId = metricId,
            DisplayName = string.IsNullOrEmpty(displayName) ? metricId : displayName,
            Unit = unit,
            IsEnabled = enabled,
            ThresholdAmount = amount,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    public static BalanceSnapshot Snapshot(
        string accountId,
        DateTimeOffset retrievedAt,
        params BalanceMetric[] metrics) =>
        new()
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = accountId,
            ProviderId = "deepseek",
            IsAvailable = true,
            RetrievedAt = retrievedAt,
            Metrics = metrics,
        };

    public static BalanceSnapshot Snapshot(
        string accountId,
        string providerId,
        string snapshotId,
        DateTimeOffset retrievedAt,
        params BalanceMetric[] metrics) =>
        new()
        {
            SnapshotId = snapshotId,
            AccountId = accountId,
            ProviderId = providerId,
            IsAvailable = true,
            RetrievedAt = retrievedAt,
            Metrics = metrics,
        };
}
