using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class NotificationPolicyEvaluatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private static ApiAccount AccountWithCnyThreshold(decimal threshold, bool enabled = true) =>
        new()
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "测试账户",
            HasCredential = true,
            CreatedAtUtc = Now.AddDays(-1),
            UpdatedAtUtc = Now.AddDays(-1),
            Monitoring = new MonitoringSettings
            {
                Thresholds = new List<BalanceThresholdRule>
                {
                    TestMetrics.CnyRule(threshold, enabled),
                },
            },
        };

    private static NotificationGlobalSettings GlobalEnabled() =>
        new()
        {
            BalanceNotificationsEnabled = true,
            DefaultRepeatIntervalHours = NotificationRepeatIntervals.DefaultHours,
            RecoveryNotificationsEnabled = true,
        };

    private static BalanceSnapshot Snapshot(string snapshotId, params BalanceMetric[] metrics) =>
        new()
        {
            SnapshotId = snapshotId,
            AccountId = "acct-1",
            ProviderId = "deepseek",
            IsAvailable = true,
            RetrievedAt = Now,
            Metrics = metrics,
        };

    private static NotificationPolicyEvaluator Evaluator() => new();

    [Fact]
    public void FirstLow_TransitionsUnknownToLow_AndNotifies()
    {
        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m),
            Snapshot("s1", TestMetrics.Cny(50m)),
            GlobalEnabled(),
            Array.Empty<NotificationStateEntry>(),
            Now);

        Assert.True(decision.ShouldNotifyLow);
        Assert.False(decision.ShouldNotifyRecovery);
        var item = Assert.Single(decision.LowItems);
        Assert.Equal("deepseek:CNY:total", item.MetricId);
        Assert.Contains("已低于阈值", item.ValueText);

        var state = Assert.Single(decision.UpdatedStates);
        Assert.Equal(NotificationStateKind.Low, state.LastState);
        Assert.Equal("s1", state.LastEvaluatedSnapshotId);
        Assert.Equal(Now, state.LastNotifiedAt);
    }

    [Fact]
    public void PersistentLow_WithinCooldown_DoesNotRepeat()
    {
        var previous = new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Low,
            LastNotifiedAt = Now.AddHours(-2),
            LastEvaluatedSnapshotId = "s-prev",
        };

        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m),
            Snapshot("s2", TestMetrics.Cny(50m)),
            GlobalEnabled(),
            new[] { previous },
            Now);

        Assert.False(decision.ShouldNotifyLow);
        Assert.Equal(NotificationStateKind.Low, Assert.Single(decision.UpdatedStates).LastState);
    }

    [Fact]
    public void PersistentLow_AfterCooldown_Repeats()
    {
        var previous = new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Low,
            LastNotifiedAt = Now.AddHours(-25),
            LastEvaluatedSnapshotId = "s-prev",
        };

        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m),
            Snapshot("s3", TestMetrics.Cny(50m)),
            GlobalEnabled(),
            new[] { previous },
            Now);

        Assert.True(decision.ShouldNotifyLow);
        Assert.Equal(Now, Assert.Single(decision.UpdatedStates).LastNotifiedAt);
    }

    [Fact]
    public void NoRepeatInterval_NeverRepeats()
    {
        var account = AccountWithCnyThreshold(100m);
        account.Notification.RepeatIntervalHours = NotificationRepeatIntervals.None;
        var previous = new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Low,
            LastNotifiedAt = Now.AddDays(-10),
            LastEvaluatedSnapshotId = "s-prev",
        };

        var decision = Evaluator().Evaluate(
            account,
            Snapshot("s4", TestMetrics.Cny(50m)),
            GlobalEnabled(),
            new[] { previous },
            Now);

        Assert.False(decision.ShouldNotifyLow);
    }

    [Fact]
    public void Recovery_FromLowToNormal_NotifiesOnce()
    {
        var previous = new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Low,
            LastNotifiedAt = Now.AddHours(-3),
            LastEvaluatedSnapshotId = "s-prev",
        };

        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m),
            Snapshot("s5", TestMetrics.Cny(150m)),
            GlobalEnabled(),
            new[] { previous },
            Now);

        Assert.True(decision.ShouldNotifyRecovery);
        Assert.False(decision.ShouldNotifyLow);
        var item = Assert.Single(decision.RecoveryItems);
        Assert.Contains("已恢复至", item.ValueText);
        Assert.Equal(NotificationStateKind.Normal, Assert.Single(decision.UpdatedStates).LastState);
    }

    [Fact]
    public void RecoveryDisabled_DoesNotNotify()
    {
        var global = GlobalEnabled();
        global.RecoveryNotificationsEnabled = false;
        var previous = new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Low,
            LastNotifiedAt = Now.AddHours(-3),
            LastEvaluatedSnapshotId = "s-prev",
        };

        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m),
            Snapshot("s6", TestMetrics.Cny(150m)),
            global,
            new[] { previous },
            Now);

        Assert.False(decision.ShouldNotifyRecovery);
    }

    [Fact]
    public void Snoozed_DoesNotNotify_WhileSnoozed()
    {
        var previous = new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Snoozed,
            LastNotifiedAt = Now.AddHours(-2),
            SnoozedUntil = Now.AddHours(20),
            LastEvaluatedSnapshotId = "s-prev",
        };

        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m),
            Snapshot("s7", TestMetrics.Cny(50m)),
            GlobalEnabled(),
            new[] { previous },
            Now);

        Assert.False(decision.ShouldNotifyLow);
        Assert.Equal(NotificationStateKind.Snoozed, Assert.Single(decision.UpdatedStates).LastState);
    }

    [Fact]
    public void SnoozeExpired_StillLow_NotifiesAfterCooldown()
    {
        var previous = new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Snoozed,
            LastNotifiedAt = Now.AddHours(-30),
            SnoozedUntil = Now.AddHours(-1),
            LastEvaluatedSnapshotId = "s-prev",
        };

        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m),
            Snapshot("s8", TestMetrics.Cny(50m)),
            GlobalEnabled(),
            new[] { previous },
            Now);

        Assert.True(decision.ShouldNotifyLow);
        Assert.Equal(NotificationStateKind.Low, Assert.Single(decision.UpdatedStates).LastState);
    }

    [Fact]
    public void SameSnapshot_IsDeduplicated()
    {
        var previous = new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Low,
            LastNotifiedAt = Now.AddHours(-1),
            LastEvaluatedSnapshotId = "s-same",
        };

        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m),
            Snapshot("s-same", TestMetrics.Cny(50m)),
            GlobalEnabled(),
            new[] { previous },
            Now);

        Assert.False(decision.ShouldNotifyLow);
    }

    [Fact]
    public void GlobalDisabled_SuppressesAndKeepsState()
    {
        var global = new NotificationGlobalSettings
        {
            BalanceNotificationsEnabled = false,
        };
        var previous = new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Unknown,
        };

        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m),
            Snapshot("s9", TestMetrics.Cny(50m)),
            global,
            new[] { previous },
            Now);

        Assert.True(decision.NotificationsSuppressed);
        Assert.False(decision.ShouldNotifyLow);
        Assert.Equal(previous, Assert.Single(decision.UpdatedStates));
    }

    [Fact]
    public void AccountDisabled_SuppressesEvenWhenGlobalEnabled()
    {
        var account = AccountWithCnyThreshold(100m);
        account.Notification.NotificationsEnabled = false;

        var decision = Evaluator().Evaluate(
            account,
            Snapshot("s10", TestMetrics.Cny(50m)),
            GlobalEnabled(),
            Array.Empty<NotificationStateEntry>(),
            Now);

        Assert.True(decision.NotificationsSuppressed);
        Assert.False(decision.ShouldNotifyLow);
    }

    [Fact]
    public void DisabledRule_DoesNotEvaluate()
    {
        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m, enabled: false),
            Snapshot("s11", TestMetrics.Cny(50m)),
            GlobalEnabled(),
            Array.Empty<NotificationStateEntry>(),
            Now);

        Assert.False(decision.ShouldNotifyLow);
        Assert.Empty(decision.UpdatedStates);
    }

    [Fact]
    public void MultipleLowMetrics_MergeIntoSingleDecision()
    {
        var account = new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "测试账户",
            HasCredential = true,
            CreatedAtUtc = Now.AddDays(-1),
            UpdatedAtUtc = Now.AddDays(-1),
            Monitoring = new MonitoringSettings
            {
                Thresholds = new List<BalanceThresholdRule>
                {
                    TestMetrics.CnyRule(100m),
                    TestMetrics.Rule("deepseek:USD:total", 100m, displayName: "USD 总余额", unit: "USD"),
                },
            },
        };

        var decision = Evaluator().Evaluate(
            account,
            Snapshot("s12", TestMetrics.Cny(50m), TestMetrics.Usd(10m)),
            GlobalEnabled(),
            Array.Empty<NotificationStateEntry>(),
            Now);

        Assert.True(decision.ShouldNotifyLow);
        Assert.Equal(2, decision.LowItems.Count);
        Assert.Equal(2, decision.UpdatedStates.Count);
    }

    [Fact]
    public void UnknownAmount_DoesNotTrigger()
    {
        var unknownMetric = new BalanceMetric
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            Kind = BalanceMetricKind.MonetaryBalance,
            AvailableAmount = null,
            TotalAmount = null,
            IsThresholdSupported = true,
        };
        var decision = Evaluator().Evaluate(
            AccountWithCnyThreshold(100m),
            Snapshot("s13", unknownMetric),
            GlobalEnabled(),
            Array.Empty<NotificationStateEntry>(),
            Now);

        Assert.False(decision.ShouldNotifyLow);
    }

    [Fact]
    public void UnlimitedMetric_NeverTriggersLow()
    {
        var account = AccountWithCnyThreshold(100m);
        var unlimitedMetric = new BalanceMetric
        {
            MetricId = "deepseek:CNY:total",
            DisplayName = "CNY 总余额",
            Unit = "CNY",
            Kind = BalanceMetricKind.MonetaryBalance,
            AvailableAmount = null,
            IsThresholdSupported = true,
            IsUnlimited = true,
        };
        var decision = Evaluator().Evaluate(
            account,
            Snapshot("s14", unlimitedMetric),
            GlobalEnabled(),
            Array.Empty<NotificationStateEntry>(),
            Now);

        Assert.False(decision.ShouldNotifyLow);
    }
}
