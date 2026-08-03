using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 托盘状态摘要与 Tooltip 文本测试（需求：正常/低余额汇总/无快照未知/
/// 查询失败保留旧状态/正在刷新/超长截断/不含 API Key）。
/// </summary>
public sealed class TrayStatusProviderTests
{
    private static ApiAccount Account(string id, decimal threshold, bool enabled) =>
        new()
        {
            AccountId = id,
            ProviderId = "deepseek",
            DisplayName = id,
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Monitoring = new MonitoringSettings
            {
                AutoRefreshEnabled = true,
                RefreshIntervalMinutes = 30,
                Thresholds = new List<BalanceThresholdRule>
                {
                    new()
                    {
                        MetricId = "deepseek:CNY:total",
                        DisplayName = "CNY 总余额",
                        Unit = "CNY",
                        IsEnabled = enabled,
                        ThresholdAmount = threshold,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    },
                },
            },
        };

    private static AccountBalanceRecord Record(string id, decimal? total, DateTimeOffset? attempt, DateTimeOffset? success)
    {
        var record = new AccountBalanceRecord
        {
            AccountId = id,
            ProviderId = "deepseek",
            LastQueryAttemptAt = attempt,
            LastQuerySuccessAt = success,
        };
        if (total is { } t)
        {
            record.LastSuccessfulSnapshot = new BalanceSnapshot
            {
                SnapshotId = Guid.NewGuid().ToString("N"),
                AccountId = id,
                ProviderId = "deepseek",
                IsAvailable = true,
                RetrievedAt = DateTimeOffset.UtcNow,
                Metrics = new List<BalanceMetric>
                {
                    new()
                    {
                        MetricId = "deepseek:CNY:total",
                        DisplayName = "CNY 总余额",
                        Unit = "CNY",
                        Kind = BalanceMetricKind.MonetaryBalance,
                        AvailableAmount = t,
                        TotalAmount = t,
                        IsThresholdSupported = true,
                    },
                },
            };
        }

        return record;
    }

    [Fact]
    public async Task NormalBalance_ShowsNormalTooltip()
    {
        var manager = new FakeAccountManager();
        manager.Accounts.Add(Account("a1", 10m, enabled: true));
        manager.Records["a1"] = Record("a1", total: 100m, attempt: DateTimeOffset.UtcNow, success: DateTimeOffset.UtcNow);

        var snapshot = await new TrayStatusProvider(manager).GetStatusAsync(CancellationToken.None);

        Assert.Equal("ApiMonitor — 余额正常", snapshot.TooltipText);
        Assert.Equal(0, snapshot.LowBalanceRuleCount);
    }

    [Fact]
    public async Task MultipleLowBalanceRules_ShowAggregateCount()
    {
        var manager = new FakeAccountManager();
        manager.Accounts.Add(Account("a1", 100m, enabled: true));
        manager.Accounts.Add(Account("a2", 50m, enabled: true));
        manager.Records["a1"] = Record("a1", total: 5m, attempt: DateTimeOffset.UtcNow, success: DateTimeOffset.UtcNow);
        manager.Records["a2"] = Record("a2", total: 3m, attempt: DateTimeOffset.UtcNow, success: DateTimeOffset.UtcNow);

        var snapshot = await new TrayStatusProvider(manager).GetStatusAsync(CancellationToken.None);

        Assert.Equal("ApiMonitor — 2 个指标低于阈值", snapshot.TooltipText);
        Assert.Equal(2, snapshot.LowBalanceRuleCount);
    }

    [Fact]
    public async Task NoSnapshot_ShowsUnknownNotLowBalance()
    {
        var manager = new FakeAccountManager();
        manager.Accounts.Add(Account("a1", 100m, enabled: true));
        manager.Records["a1"] = Record("a1", total: null, attempt: null, success: null);

        var snapshot = await new TrayStatusProvider(manager).GetStatusAsync(CancellationToken.None);

        Assert.Equal("ApiMonitor — 尚无余额数据", snapshot.TooltipText);
        Assert.Equal(0, snapshot.LowBalanceRuleCount);
    }

    [Fact]
    public async Task FailedRefresh_KeepsLastSuccessfulState()
    {
        var manager = new FakeAccountManager();
        manager.Accounts.Add(Account("a1", 10m, enabled: true));
        // 最近一次尝试失败：attempt > success，但 LastSuccessfulSnapshot 仍保留旧值。
        manager.Records["a1"] = Record(
            "a1",
            total: 100m,
            attempt: DateTimeOffset.UtcNow,
            success: DateTimeOffset.UtcNow.AddMinutes(-5));

        var snapshot = await new TrayStatusProvider(manager).GetStatusAsync(CancellationToken.None);

        Assert.Equal("ApiMonitor — 余额正常；最近刷新失败", snapshot.TooltipText);
        Assert.True(snapshot.HasRecentFailure);
        Assert.Equal(0, snapshot.LowBalanceRuleCount);
    }

    [Fact]
    public async Task Refreshing_ShowsRefreshingStatus()
    {
        var manager = new FakeAccountManager();
        manager.Accounts.Add(Account("a1", 10m, enabled: true));
        manager.Records["a1"] = Record("a1", total: 100m, attempt: DateTimeOffset.UtcNow, success: DateTimeOffset.UtcNow);
        manager.ActiveRefreshCount = 1;

        var snapshot = await new TrayStatusProvider(manager).GetStatusAsync(CancellationToken.None);

        Assert.Equal("ApiMonitor — 余额正常；正在刷新", snapshot.TooltipText);
        Assert.True(snapshot.IsRefreshing);
    }

    [Fact]
    public void TooltipText_NeverContainsApiKeyMaterial()
    {
        string[] outputs =
        {
            TrayStatusText.TooltipFor(0, hasAnySnapshot: false, isRefreshing: false, hasRecentFailure: false),
            TrayStatusText.TooltipFor(3, hasAnySnapshot: true, isRefreshing: false, hasRecentFailure: false),
            TrayStatusText.TooltipFor(0, hasAnySnapshot: true, isRefreshing: true, hasRecentFailure: false),
        };

        foreach (string text in outputs)
        {
            Assert.False(text.Contains("sk-", StringComparison.OrdinalIgnoreCase));
            Assert.False(text.Contains("key", StringComparison.OrdinalIgnoreCase));
            Assert.False(text.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
            Assert.True(text.Length <= 127, $"Tooltip 超长: {text}");
        }
    }
}
