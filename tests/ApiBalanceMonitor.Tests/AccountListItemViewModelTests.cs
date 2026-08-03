using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.ViewModels;
using Xunit;

namespace ApiBalanceMonitor.Tests;

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

    private static BalanceSnapshot Snapshot(DateTimeOffset retrievedAt, params BalanceAmount[] balances) =>
        new()
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            IsAvailable = true,
            RetrievedAt = retrievedAt,
            Balances = balances,
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
        var cny = new BalanceAmount { Currency = "CNY", TotalBalance = 10m, GrantedBalance = 0m, ToppedUpBalance = 10m };
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
            Currency = "CNY",
            IsEnabled = true,
            ThresholdAmount = 20m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        item.RefreshDisplay();
        Assert.Equal("CNY 余额低于阈值 20.00", item.ThresholdSummaryText);
        Assert.True(item.IsLowBalance);

        // 等于阈值 → 正常
        item.ApplySnapshot(Snapshot(DateTimeOffset.UtcNow, new BalanceAmount
        {
            Currency = "CNY",
            TotalBalance = 20m,
            GrantedBalance = 0m,
            ToppedUpBalance = 20m,
        }));
        Assert.Equal("余额正常", item.ThresholdSummaryText);
        Assert.False(item.IsLowBalance);
    }

    [Fact]
    public void ThresholdSummary_MultipleCurrenciesBelow_ShowsCount()
    {
        var item = CreateItem();
        item.ApplySnapshot(Snapshot(
            DateTimeOffset.UtcNow,
            new BalanceAmount { Currency = "CNY", TotalBalance = 5m, GrantedBalance = 0m, ToppedUpBalance = 5m },
            new BalanceAmount { Currency = "USD", TotalBalance = 1m, GrantedBalance = 0m, ToppedUpBalance = 1m }));
        item.Account.Monitoring.Thresholds.Add(new BalanceThresholdRule
        {
            Currency = "CNY",
            IsEnabled = true,
            ThresholdAmount = 20m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        item.Account.Monitoring.Thresholds.Add(new BalanceThresholdRule
        {
            Currency = "USD",
            IsEnabled = true,
            ThresholdAmount = 10m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        item.RefreshDisplay();

        Assert.Equal("2 个币种低于阈值", item.ThresholdSummaryText);
        Assert.True(item.IsLowBalance);
    }

    [Fact]
    public void ThresholdRuleChange_RecomputesWithoutQuery()
    {
        var item = CreateItem();
        item.ApplySnapshot(Snapshot(DateTimeOffset.UtcNow, new BalanceAmount
        {
            Currency = "CNY",
            TotalBalance = 30m,
            GrantedBalance = 0m,
            ToppedUpBalance = 30m,
        }));
        item.Account.Monitoring.Thresholds.Add(new BalanceThresholdRule
        {
            Currency = "CNY",
            IsEnabled = true,
            ThresholdAmount = 50m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        item.RefreshDisplay();
        Assert.Equal("CNY 余额低于阈值 50.00", item.ThresholdSummaryText);

        item.Account.Monitoring.Thresholds[0].ThresholdAmount = 10m;
        item.RefreshDisplay();
        Assert.Equal("余额正常", item.ThresholdSummaryText);
    }
}
