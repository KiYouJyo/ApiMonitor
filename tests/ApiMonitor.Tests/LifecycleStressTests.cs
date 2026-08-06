using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v1.0.0：受限时长（数秒级）的重复生命周期/压力模拟，覆盖长时间运行的
/// 关键状态：文件句柄释放、调度器不重复、事件订阅不累积、窗口开关不泄漏。
/// 不等待真实 8-24 小时（CI 策略），正式长时间运行由人工验收完成。
/// </summary>
public sealed class LifecycleStressTests
{
    [Fact]
    public async Task OnboardingStore_ManyWriteLoadCycles_StayConsistentAndLockFree()
    {
        using var temp = new TempDirectory();
        var store = new JsonOnboardingStateStore(temp.Path);

        for (int i = 0; i < 200; i++)
        {
            await store.MarkCompletedAsync(skipped: i % 2 == 0, CancellationToken.None);
            var data = await store.LoadAsync(CancellationToken.None);
            Assert.True(data.OnboardingCompleted);
            Assert.Equal(i % 2 == 0, data.OnboardingSkipped);
        }

        // 无残留临时文件（文件句柄已释放）。
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
        Assert.Single(Directory.GetFiles(temp.Path, JsonOnboardingStateStore.FileName));
    }

    [Fact]
    public async Task Scheduler_ManyStartStopCycles_NoDuplicateLoops()
    {
        using var temp = new TempDirectory();
        var accounts = new FakeAccountManager();
        var log = new AppLog(temp.Path);
        var scheduler = new MonitoringScheduler(accounts, TimeProvider.System, log);

        using var appToken = new CancellationTokenSource();
        for (int i = 0; i < 50; i++)
        {
            scheduler.Start(appToken.Token);
            Assert.True(scheduler.IsRunning);
            scheduler.Start(appToken.Token); // 重复 Start 必须是无操作
            await Task.Delay(10);
            scheduler.Stop();
            Assert.False(scheduler.IsRunning);
            scheduler.Stop(); // 重复 Stop 必须是无操作
        }

        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void WindowLifecycleTracker_ManyOpenCloseCycles_NoEventAccumulation()
    {
        var tracker = new WindowLifecycleTracker();
        int closedEvents = 0;
        tracker.AllWindowsClosed += () => closedEvents++;

        for (int i = 0; i < 200; i++)
        {
            tracker.MainWindowOpened();
            tracker.MainWindowClosed();
            tracker.FloatingWindowOpened();
            tracker.FloatingWindowClosed();
        }

        // 每轮“开/关主窗口”与“开/关悬浮窗”各触发一次空窗事件。
        Assert.Equal(400, closedEvents);
        Assert.False(tracker.IsMainWindowOpen);
        Assert.False(tracker.IsFloatingWindowOpen);
    }

    [Fact]
    public async Task HealthService_ManyRuns_AreStableAndDoNotGrow()
    {
        using var temp = new TempDirectory();
        var channel = new DistributionChannelService("1.0.0", "1.0.0.1");
        var accounts = new FakeAccountManager();
        var registry = new Providers.ProviderRegistry(new Providers.IApiBalanceProvider[]
        {
            new StressFakeProvider("deepseek"),
            new StressFakeProvider("openrouter"),
        });
        var notification = new FakeAppNotificationService();
        notification.Register();
        var scheduler = new FakeMonitoringScheduler();
        var health = new AppHealthService(
            channel,
            new FakeSecretStore(),
            new JsonAccountStore(temp.Path),
            new JsonBalanceSnapshotStore(temp.Path),
            new JsonAppearanceSettingsStore(temp.Path),
            registry,
            notification,
            _ => Task.FromResult(true),
            new FakeTrayIconService { IsActive = true },
            new StressStartupTask(),
            scheduler,
            () => true,
            () => false,
            accounts,
            new StressUpdateService());

        for (int i = 0; i < 20; i++)
        {
            var results = await health.RunAsync(CancellationToken.None);
            Assert.Equal(21, results.Count);
            Assert.DoesNotContain(results, r => r.Status == HealthStatus.Failed);
        }
    }

    private sealed class StressFakeProvider : Providers.IApiBalanceProvider
    {
        public StressFakeProvider(string id)
        {
            ProviderId = id;
            DisplayName = id;
            Info = new Providers.ProviderInfo(
                id, id, id, true, false,
                Array.Empty<Models.BalanceMetricKind>(),
                Array.Empty<Providers.ProviderCredentialOption>(),
                string.Empty, string.Empty, false);
        }

        public string ProviderId { get; }

        public string DisplayName { get; }

        public Providers.ProviderInfo Info { get; }

        public System.Threading.Tasks.Task<Models.BalanceQueryResult> QueryBalanceAsync(
            Models.ApiAccount account,
            System.Collections.Generic.IReadOnlyDictionary<string, string> credentials,
            System.Threading.CancellationToken cancellationToken) =>
            throw new System.NotSupportedException();
    }

    private sealed class StressStartupTask : IStartupTaskService
    {
        public Models.StartupTaskStatus? CachedStatus => Models.StartupTaskStatus.Disabled;

        public System.Threading.Tasks.Task<Models.StartupTaskStatus> RefreshStatusAsync(
            System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult(Models.StartupTaskStatus.Disabled);

        public System.Threading.Tasks.Task<Models.StartupTaskStatus> EnableAsync(
            System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult(Models.StartupTaskStatus.Disabled);

        public System.Threading.Tasks.Task<Models.StartupTaskStatus> DisableAsync(
            System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult(Models.StartupTaskStatus.Disabled);
    }

    private sealed class StressUpdateService : IUpdateService
    {
        public DistributionChannel Channel => DistributionChannel.Development;

        public System.Threading.Tasks.Task<UpdateCheckResult> CheckAsync(
            System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult(
                new UpdateCheckResult { Status = UpdateCheckStatus.DevelopmentBuild });
    }
}
