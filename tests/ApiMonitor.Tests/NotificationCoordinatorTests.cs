using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class NotificationCoordinatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private static ApiAccount AccountWithThreshold(decimal threshold, bool notificationsEnabled = true) =>
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
                Thresholds = new List<BalanceThresholdRule> { TestMetrics.CnyRule(threshold) },
            },
            Notification = new AccountNotificationSettings
            {
                NotificationsEnabled = notificationsEnabled,
            },
        };

    private static (NotificationCoordinator Coordinator, FakeAccountManager Accounts, FakeNotificationStateStore States,
        FakeNotificationSettingsStore Settings, FakeAppNotificationService Notifications, FakeTimeProvider Time) CreateSut()
    {
        var accounts = new FakeAccountManager();
        accounts.Accounts.Add(AccountWithThreshold(100m));
        var states = new FakeNotificationStateStore();
        var settings = new FakeNotificationSettingsStore
        {
            Settings = new NotificationGlobalSettings
            {
                BalanceNotificationsEnabled = true,
                DefaultRepeatIntervalHours = 24,
                RecoveryNotificationsEnabled = true,
            },
        };
        var notifications = new FakeAppNotificationService();
        var time = new FakeTimeProvider { UtcNow = Now };
        var coordinator = new NotificationCoordinator(
            accounts,
            states,
            settings,
            new NotificationPolicyEvaluator(),
            notifications,
            new AppLog(System.IO.Path.GetTempPath()),
            time);
        return (coordinator, accounts, states, settings, notifications, time);
    }

    [Fact]
    public async Task SuccessfulLowSnapshot_ShowsOneNotificationAndPersistsState()
    {
        var (coordinator, _, states, _, notifications, _) = CreateSut();

        await coordinator.InitializeAsync(CancellationToken.None);
        var result = BalanceQueryResult.Success(TestMetrics.Snapshot(
            "acct-1",
            Now,
            TestMetrics.Cny(50m)));
        await coordinator.HandleRefreshCompletedAsync(
            new AccountRefreshCompletedEventArgs
            {
                AccountId = "acct-1",
                Result = result,
                Source = BalanceQuerySource.Automatic,
            },
            CancellationToken.None);

        Assert.Equal(1, notifications.LowNotificationsShown);
        Assert.Equal(0, notifications.RecoveryNotificationsShown);
        Assert.Equal("ApiMonitor-acct-1", notifications.LastTag);
        var state = Assert.Single(states.States);
        Assert.Equal(NotificationStateKind.Low, state.LastState);
        Assert.Equal("ApiMonitor-acct-1", state.LastNotificationTag);
        Assert.True(states.SaveCalls >= 1);
    }

    [Fact]
    public async Task FailedQuery_DoesNotNotifyAndDoesNotChangeState()
    {
        var (coordinator, _, states, _, notifications, _) = CreateSut();
        await coordinator.InitializeAsync(CancellationToken.None);

        await coordinator.HandleRefreshCompletedAsync(
            new AccountRefreshCompletedEventArgs
            {
                AccountId = "acct-1",
                Result = BalanceQueryResult.Failure(BalanceErrorKind.Network, "网络错误"),
                Source = BalanceQuerySource.Automatic,
            },
            CancellationToken.None);

        Assert.Equal(0, notifications.LowNotificationsShown);
        Assert.Empty(states.States);
        Assert.Equal(0, states.SaveCalls);
    }

    [Fact]
    public async Task Snooze_SetsSnoozedUntilAndDoesNotOpenWindow()
    {
        var (coordinator, _, states, _, notifications, time) = CreateSut();
        await coordinator.InitializeAsync(CancellationToken.None);
        states.States.Add(new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Low,
            LastNotifiedAt = Now.AddHours(-1),
        });

        await coordinator.SnoozeAsync("acct-1", "deepseek:CNY:total", CancellationToken.None);

        var state = Assert.Single(states.States);
        Assert.Equal(NotificationStateKind.Snoozed, state.LastState);
        Assert.Equal(Now.AddHours(24), state.SnoozedUntil);
        Assert.Equal(0, notifications.TestNotificationsShown);
    }

    [Fact]
    public async Task RemoveAccount_CleansNotificationsAndStateOnlyForThatAccount()
    {
        var (coordinator, _, states, _, notifications, _) = CreateSut();
        await coordinator.InitializeAsync(CancellationToken.None);
        states.States.Add(new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Low,
        });
        states.States.Add(new NotificationStateEntry
        {
            AccountId = "acct-other",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Low,
        });

        await coordinator.RemoveAccountAsync("acct-1", CancellationToken.None);

        Assert.Contains("acct-1", notifications.RemovedAccountIds);
        Assert.Contains("acct-1", states.DeletedAccountIds);
        var remaining = Assert.Single(states.States);
        Assert.Equal("acct-other", remaining.AccountId);
    }

    [Fact]
    public async Task TestNotification_DoesNotTouchStateOrHistory()
    {
        var (coordinator, _, states, _, notifications, _) = CreateSut();
        await coordinator.InitializeAsync(CancellationToken.None);

        coordinator.ShowTestNotification();

        Assert.Equal(1, notifications.TestNotificationsShown);
        Assert.Empty(states.States);
    }
}
