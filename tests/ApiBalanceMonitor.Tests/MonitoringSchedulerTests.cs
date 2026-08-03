using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Services;
using ApiBalanceMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiBalanceMonitor.Tests;

public sealed class MonitoringSchedulerTests
{
    private static (MonitoringScheduler Scheduler, FakeAccountManager Manager, FakeTimeProvider Time) CreateSut()
    {
        var manager = new FakeAccountManager();
        var time = new FakeTimeProvider();
        var scheduler = new MonitoringScheduler(manager, time, new AppLog(Path.GetTempPath()));
        return (scheduler, manager, time);
    }

    [Fact]
    public async Task Tick_RefreshesEachDueAccountOnceWithAutomaticSource()
    {
        var (scheduler, manager, _) = CreateSut();
        manager.DueAccountIds.AddRange(new[] { "acct-a", "acct-b" });

        await scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(2, manager.RefreshCalls);
        Assert.All(manager.RefreshedAccountIds, id => Assert.Contains(id, manager.DueAccountIds));
        Assert.All(manager.RefreshedAccountIds, _ => Assert.Equal(BalanceQuerySource.Automatic, manager.LastRefreshSource));
    }

    [Fact]
    public async Task Tick_NoDueAccounts_DoesNotQuery()
    {
        var (scheduler, manager, _) = CreateSut();
        manager.DueAccountIds.Clear();

        await scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(0, manager.RefreshCalls);
    }

    [Fact]
    public async Task StartWithCancelledToken_StopsWithoutQuerying()
    {
        var (scheduler, manager, _) = CreateSut();
        manager.DueAccountIds.Add("acct-a");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        scheduler.Start(cts.Token);
        await Task.Delay(300);
        scheduler.Stop();

        Assert.Equal(0, manager.RefreshCalls);
    }

    [Fact]
    public async Task Start_RunsImmediateCatchUpTick()
    {
        var (scheduler, manager, _) = CreateSut();
        manager.DueAccountIds.Add("acct-a");
        using var cts = new CancellationTokenSource();

        scheduler.Start(cts.Token);
        await Task.Delay(400);
        scheduler.Stop();

        Assert.Equal(1, manager.RefreshCalls);
    }

    [Fact]
    public async Task Tick_OneAccountFailing_DoesNotBlockOthers()
    {
        var (scheduler, manager, _) = CreateSut();
        manager.DueAccountIds.AddRange(new[] { "acct-a", "acct-b" });
        manager.RefreshResult = BalanceQueryResult.Failure(BalanceErrorKind.Network, "网络错误");

        await scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(2, manager.RefreshCalls);
    }
}
