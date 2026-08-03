using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class NotificationActivationRouterTests
{
    private sealed class WindowCounter
    {
        public int Count { get; set; }
    }

    private static (NotificationActivationRouter Router, FakeAccountManager Accounts, WindowCounter Window, List<string> Focused,
        List<(string Title, string Message)> Messages, FakeNotificationStateStore States) CreateSut()
    {
        var accounts = new FakeAccountManager();
        var states = new FakeNotificationStateStore();
        var settings = new FakeNotificationSettingsStore();
        var notifications = new FakeAppNotificationService();
        var window = new WindowCounter();
        var focused = new List<string>();
        var messages = new List<(string, string)>();
        var coordinator = new NotificationCoordinator(
            accounts,
            states,
            settings,
            new NotificationPolicyEvaluator(),
            notifications,
            new AppLog(System.IO.Path.GetTempPath()),
            new FakeTimeProvider());
        var router = new NotificationActivationRouter(
            accounts,
            coordinator,
            () => window.Count++,
            id => focused.Add(id),
            (title, message) => messages.Add((title, message)));
        return (router, accounts, window, focused, messages, states);
    }

    [Fact]
    public async Task OpenAccount_ShowsWindowAndFocusesAccount()
    {
        var (router, accounts, window, focused, _, _) = CreateSut();
        accounts.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "A",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await router.HandleAsync(
            new NotificationActivationPayload(
                NotificationActions.OpenAccount,
                "acct-1",
                "deepseek",
                "deepseek:CNY:total"),
            CancellationToken.None);

        Assert.Equal(1, window.Count);
        Assert.Equal("acct-1", Assert.Single(focused));
    }

    [Fact]
    public async Task OpenDeletedAccount_ShowsWindowAndMessage_WithoutCrash()
    {
        var (router, _, window, focused, messages, _) = CreateSut();

        await router.HandleAsync(
            new NotificationActivationPayload(
                NotificationActions.OpenAccount,
                "gone-account",
                "deepseek",
                "deepseek:CNY:total"),
            CancellationToken.None);

        Assert.Equal(1, window.Count);
        Assert.Empty(focused);
        var message = Assert.Single(messages);
        Assert.Equal("账户不存在", message.Title);
    }

    [Fact]
    public async Task SnoozeAction_DoesNotShowWindow()
    {
        var (router, _, window, _, _, states) = CreateSut();
        states.States.Add(new NotificationStateEntry
        {
            AccountId = "acct-1",
            MetricId = "deepseek:CNY:total",
            LastState = NotificationStateKind.Low,
        });

        await router.HandleAsync(
            new NotificationActivationPayload(
                NotificationActions.Snooze24Hours,
                "acct-1",
                "deepseek",
                "deepseek:CNY:total"),
            CancellationToken.None);

        Assert.Equal(0, window.Count);
        Assert.Equal(NotificationStateKind.Snoozed, Assert.Single(states.States).LastState);
        Assert.NotNull(Assert.Single(states.States).SnoozedUntil);
    }

    [Fact]
    public async Task TestAction_ShowsWindowOnly()
    {
        var (router, _, window, focused, _, _) = CreateSut();

        await router.HandleAsync(
            new NotificationActivationPayload(NotificationActions.Test, null, null, null),
            CancellationToken.None);

        Assert.Equal(1, window.Count);
        Assert.Empty(focused);
    }
}
