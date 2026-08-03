using ApiMonitor.Models;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class AccountListItemViewModelTests
{
    private static ApiAccount Account(bool hasCredential = true) =>
        new()
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "测试账户",
            HasCredential = hasCredential,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static BalanceMetric CnyMetric(decimal total) =>
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

    private static BalanceMetric UsdMetric(decimal total) =>
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

    private static BalanceSnapshot Snapshot(DateTimeOffset retrievedAt, params BalanceMetric[] metrics) =>
        new()
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = "acct-1",
            ProviderId = "deepseek",
            IsAvailable = true,
            RetrievedAt = retrievedAt,
            Metrics = metrics,
        };

    private static AccountListItemViewModel CreateItem(
        ApiAccount? account = null,
        AccountBalanceRecord? record = null) =>
        new(
            account ?? Account(),
            "DeepSeek",
            record,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);

    [Fact]
    public void WithoutSuccessRecord_ShowsPlaceholder()
    {
        var item = CreateItem();

        Assert.Equal("最近成功更新：尚未成功更新", item.LastSuccessLine);
    }

    [Fact]
    public void WithSnapshot_ShowsLocalTimeInStableFormat()
    {
        var retrievedAt = new DateTimeOffset(2026, 8, 3, 0, 30, 0, TimeSpan.Zero);
        var item = CreateItem();

        item.ApplySnapshot(Snapshot(retrievedAt));

        string expected = "最近成功更新：" + retrievedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        Assert.Equal(expected, item.LastSuccessLine);
    }

    [Fact]
    public void RefreshSuccess_UpdatesLastSuccessLine()
    {
        var item = CreateItem();
        var first = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 8, 3, 1, 15, 0, TimeSpan.Zero);

        item.ApplySnapshot(Snapshot(first));
        string before = item.LastSuccessLine;

        item.ApplySnapshot(Snapshot(second));

        Assert.NotEqual(before, item.LastSuccessLine);
        Assert.Equal(
            "最近成功更新：" + second.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            item.LastSuccessLine);
    }

    [Fact]
    public void RefreshFailure_DoesNotClearLastSuccessTime()
    {
        var retrievedAt = new DateTimeOffset(2026, 8, 3, 0, 45, 0, TimeSpan.Zero);
        var item = CreateItem();
        item.ApplySnapshot(Snapshot(retrievedAt));
        string before = item.LastSuccessLine;

        item.ApplyError(new BalanceQueryError(BalanceErrorKind.Unauthorized, "API Key 无效"));

        Assert.Equal(before, item.LastSuccessLine);
        Assert.Contains("API Key 无效", item.LastErrorText);
    }

    [Fact]
    public void CopyKeyCommand_DisabledWhenAccountHasNoCredential()
    {
        var item = CreateItem(Account(hasCredential: false));

        Assert.False(item.CopyKeyCommand.CanExecute(null));
    }

    [Fact]
    public void NextRefreshText_ReflectsMonitoringState()
    {
        var account = Account();
        account.Monitoring.AutoRefreshEnabled = false;
        var disabled = CreateItem(account);
        Assert.Equal("下次刷新：自动刷新已关闭", disabled.NextRefreshText);

        account.Monitoring.AutoRefreshEnabled = true;
        account.Monitoring.NextRefreshAtUtc = null;
        Assert.Equal("下次刷新：尚未查询", disabled.NextRefreshText);

        var next = new DateTimeOffset(2026, 8, 3, 2, 0, 0, TimeSpan.Zero);
        account.Monitoring.NextRefreshAtUtc = next;
        Assert.Equal(
            "下次刷新：" + next.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            disabled.NextRefreshText);
    }

    [Fact]
    public void AutoRefreshStatusAndIntervalText_AreDisplayed()
    {
        var account = Account();
        account.Monitoring.AutoRefreshEnabled = true;
        account.Monitoring.RefreshIntervalMinutes = 30;
        var item = CreateItem(account);

        Assert.Equal("自动刷新：已开启", item.AutoRefreshStatusText);
        Assert.Equal("刷新间隔：30 分钟", item.RefreshIntervalText);
    }

    [Fact]
    public void ThresholdSummary_BelowEqualDisabledAndUnknown()
    {
        var cny = CnyMetric(10m);
        var item = CreateItem();

        // 无数据
        Assert.Equal("尚无余额数据", item.ThresholdSummaryText);
        Assert.False(item.IsLowBalance);

        // 未启用规则
        item.ApplySnapshot(Snapshot(DateTimeOffset.UtcNow, cny));
        Assert.Equal("未启用提醒", item.ThresholdSummaryText);

        // 低于阈值
        item.Account.Monitoring.Thresholds.Add(new BalanceThresholdRule
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            IsEnabled = true,
            ThresholdAmount = 20m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        item.RefreshDisplay();
        Assert.Equal("CNY 总余额 低于阈值 20.00", item.ThresholdSummaryText);
        Assert.True(item.IsLowBalance);

        // 等于阈值 → 正常
        item.ApplySnapshot(Snapshot(DateTimeOffset.UtcNow, CnyMetric(20m)));
        Assert.Equal("余额正常", item.ThresholdSummaryText);
        Assert.False(item.IsLowBalance);
    }

    [Fact]
    public void ThresholdSummary_MultipleCurrenciesBelow_ShowsCount()
    {
        var item = CreateItem();
        item.ApplySnapshot(Snapshot(
            DateTimeOffset.UtcNow,
            CnyMetric(5m),
            UsdMetric(1m)));
        item.Account.Monitoring.Thresholds.Add(new BalanceThresholdRule
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            IsEnabled = true,
            ThresholdAmount = 20m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        item.Account.Monitoring.Thresholds.Add(new BalanceThresholdRule
        {
            MetricId = "deepseek:USD:total",
            DisplayName = "USD 总余额",
            Unit = "USD",
            IsEnabled = true,
            ThresholdAmount = 10m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        item.RefreshDisplay();

        Assert.Equal("2 个指标低于阈值", item.ThresholdSummaryText);
        Assert.True(item.IsLowBalance);
    }

    [Fact]
    public void ThresholdRuleChange_RecomputesWithoutQuery()
    {
        var item = CreateItem();
        item.ApplySnapshot(Snapshot(DateTimeOffset.UtcNow, CnyMetric(30m)));
        item.Account.Monitoring.Thresholds.Add(new BalanceThresholdRule
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            IsEnabled = true,
            ThresholdAmount = 50m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        item.RefreshDisplay();
        Assert.Equal("CNY 总余额 低于阈值 50.00", item.ThresholdSummaryText);

        item.Account.Monitoring.Thresholds[0].ThresholdAmount = 10m;
        item.RefreshDisplay();
        Assert.Equal("余额正常", item.ThresholdSummaryText);
    }

    [Fact]
    public void DeepSeekCard_ShowsFullCurrencyMetrics()
    {
        var metric = new BalanceMetric
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            Kind = BalanceMetricKind.MonetaryBalance,
            AvailableAmount = 110.00m,
            TotalAmount = 110.00m,
            GrantedAmount = 10.00m,
            ToppedUpAmount = 100.00m,
            IsThresholdSupported = true,
        };
        var item = CreateItem();

        item.ApplySnapshot(Snapshot(DateTimeOffset.UtcNow, metric));

        var line = Assert.Single(item.BalanceLines);
        Assert.Equal("CNY · 总额 110.00 · 赠送 10.00 · 充值 100.00", line.LineText);
    }

    [Fact]
    public void OpenRouterApiKeyCard_ShowsQuotaAndUsageMetrics()
    {
        var quota = new BalanceMetric
        {
            MetricId = "openrouter:key:quota-remaining",
            DisplayName = "密钥剩余额度",
            Unit = "credits",
            Kind = BalanceMetricKind.KeyQuota,
            AvailableAmount = 4.25m,
            TotalAmount = 10.00m,
            IsThresholdSupported = true,
        };
        var usage = new BalanceMetric
        {
            MetricId = "openrouter:key:usage-total",
            DisplayName = "累计使用量",
            Unit = "credits",
            Kind = BalanceMetricKind.Usage,
            UsedAmount = 5.75m,
        };
        var item = CreateItem();

        item.ApplySnapshot(Snapshot(DateTimeOffset.UtcNow, quota, usage));

        Assert.Equal(2, item.BalanceLines.Count);
        Assert.Contains(item.BalanceLines, l => l.LineText.Contains("密钥剩余额度 4.25"));
        Assert.Contains(item.BalanceLines, l => l.LineText.Contains("上限 10.00"));
        Assert.Contains(item.BalanceLines, l => l.LineText.Contains("累计使用量 5.75"));
    }

    [Fact]
    public void OpenRouterManagementCard_ShowsCreditsMetrics()
    {
        var credits = new BalanceMetric
        {
            MetricId = "openrouter:credits:remaining",
            DisplayName = "剩余 Credits",
            Unit = "credits",
            Kind = BalanceMetricKind.PlatformCredits,
            AvailableAmount = 4.25m,
            TotalAmount = 10.00m,
            UsedAmount = 5.75m,
            IsThresholdSupported = true,
        };
        var item = CreateItem();

        item.ApplySnapshot(Snapshot(DateTimeOffset.UtcNow, credits));

        var line = Assert.Single(item.BalanceLines);
        Assert.Equal("剩余 Credits 4.25 · 累计充值 10.00 · 累计使用 5.75", line.LineText);
    }

    [Fact]
    public void NullGrantedAndToppedUp_ShowUnknownNotZero()
    {
        var metric = new BalanceMetric
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            Kind = BalanceMetricKind.MonetaryBalance,
            AvailableAmount = 88.00m,
            TotalAmount = 88.00m,
            IsThresholdSupported = true,
        };
        var item = CreateItem();

        item.ApplySnapshot(Snapshot(DateTimeOffset.UtcNow, metric));

        var line = Assert.Single(item.BalanceLines);
        Assert.Contains("赠送 未知", line.LineText);
        Assert.Contains("充值 未知", line.LineText);
        Assert.DoesNotContain("赠送 0.00", line.LineText);
    }

    [Fact]
    public void CredentialModeText_ReflectsProviderAndMode()
    {
        var deepseek = CreateItem();
        Assert.Equal(string.Empty, deepseek.CredentialModeText);
        Assert.False(deepseek.HasCredentialModeText);

        var orKey = CreateItem(AccountWithProvider("openrouter", "api-key"));
        Assert.Equal("普通 API Key", orKey.CredentialModeText);
        Assert.True(orKey.HasCredentialModeText);

        var orMgmt = CreateItem(AccountWithProvider("openrouter", "management-key"));
        Assert.Equal("Management Key", orMgmt.CredentialModeText);
    }

    [Fact]
    public void NotificationsEnabledText_ReflectsTriState()
    {
        var inherited = CreateItem();
        Assert.Equal("通知：继承全局", inherited.NotificationsEnabledText);

        var enabled = Account();
        enabled.Notification.NotificationsEnabled = true;
        Assert.Equal("通知：开启", CreateItem(enabled).NotificationsEnabledText);

        var disabled = Account();
        disabled.Notification.NotificationsEnabled = false;
        Assert.Equal("通知：关闭", CreateItem(disabled).NotificationsEnabledText);
    }

    [Fact]
    public void SnoozeSummaryText_DrivesHasSnooze()
    {
        var item = CreateItem();

        Assert.False(item.HasSnooze);

        item.SnoozeSummaryText = "暂停提醒至 2026-08-04 08:00";
        Assert.True(item.HasSnooze);

        item.SnoozeSummaryText = string.Empty;
        Assert.False(item.HasSnooze);
    }

    [Fact]
    public void RefreshCommand_IsBoundToAccountId()
    {
        var captured = new List<string>();
        var account = new ApiAccount
        {
            AccountId = "acct-bound",
            ProviderId = "deepseek",
            DisplayName = "绑定测试",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var item = new AccountListItemViewModel(
            account,
            "DeepSeek",
            null,
            () =>
            {
                captured.Add("acct-bound");
                return Task.CompletedTask;
            },
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);

        item.RefreshCommand.Execute(null);

        Assert.Equal("acct-bound", Assert.Single(captured));
    }

    private static ApiAccount AccountWithProvider(string providerId, string? credentialMode)
    {
        return new ApiAccount
        {
            AccountId = "acct-" + providerId,
            ProviderId = providerId,
            DisplayName = providerId + " 账户",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CredentialMode = credentialMode,
        };
    }
}
