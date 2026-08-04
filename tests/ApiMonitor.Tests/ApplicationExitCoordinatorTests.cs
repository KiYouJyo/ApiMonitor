using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 统一退出协调器测试（需求：退出流程幂等最多执行一次、顺序正确、
/// 多次点击退出不崩溃、中途失败不阻塞进程退出）。
/// </summary>
public sealed class ApplicationExitCoordinatorTests
{
    private sealed class Harness
    {
        public FakeMonitoringScheduler Scheduler { get; } = new();

        public FakeTrayIconService Tray { get; } = new();

        public FakeFloatingWindowService Floating { get; } = new();

        public FakeTraySettingsStore SettingsStore { get; } = new();

        public int CancelCalls { get; private set; }

        public int CloseMainWindowCalls { get; private set; }

        public int ExitProcessCalls { get; private set; }

        public AppLog Log { get; } = new(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"abm-exit-log-{Guid.NewGuid():N}"));

        public ApplicationExitCoordinator CreateSut()
        {
            return new ApplicationExitCoordinator(
                Scheduler,
                () => Tray,
                Floating,
                SettingsStore,
                () => CancelCalls++,
                () => CloseMainWindowCalls++,
                () => ExitProcessCalls++,
                Log);
        }
    }

    private static async Task WaitForExitProcessAsync(Harness harness)
    {
        for (int i = 0; i < 50 && harness.ExitProcessCalls == 0; i++)
        {
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task BeginExit_RunsFullSequenceInOrder()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();

        sut.BeginExit();
        await WaitForExitProcessAsync(harness);

        Assert.True(sut.IsExiting);
        Assert.Equal(1, harness.Scheduler.StopCalls);
        Assert.Equal(1, harness.CancelCalls);
        Assert.Equal(1, harness.Tray.ShutdownCalls);
        Assert.Equal(1, harness.Floating.ShutdownCalls);
        Assert.Equal(1, harness.CloseMainWindowCalls);
        Assert.Equal(1, harness.ExitProcessCalls);
        Assert.Equal(1, harness.SettingsStore.SaveCalls);
    }

    [Fact]
    public async Task BeginExit_IsIdempotent()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();

        sut.BeginExit();
        sut.BeginExit();
        sut.BeginExit();
        await WaitForExitProcessAsync(harness);

        Assert.Equal(1, harness.Tray.ShutdownCalls);
        Assert.Equal(1, harness.ExitProcessCalls);
        Assert.Equal(1, harness.Scheduler.StopCalls);
    }

    [Fact]
    public async Task SettingsFailure_DoesNotBlockProcessExit()
    {
        var harness = new Harness();
        harness.SettingsStore.ThrowOnLoad = true;
        var sut = harness.CreateSut();

        sut.BeginExit();
        await WaitForExitProcessAsync(harness);

        Assert.Equal(1, harness.ExitProcessCalls);
    }
}
