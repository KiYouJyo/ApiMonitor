using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>窗口生命周期纯逻辑测试：最后一个窗口关闭才触发退出。</summary>
public sealed class WindowLifecycleTrackerTests
{
    [Fact]
    public void MainClosedWhileCompactOpen_DoesNotExit()
    {
        var tracker = new WindowLifecycleTracker();
        int exitCount = 0;
        tracker.AllWindowsClosed += () => exitCount++;

        tracker.MainWindowOpened();
        tracker.CompactWindowOpened();
        tracker.MainWindowClosed();

        Assert.Equal(0, exitCount);
        Assert.True(tracker.IsCompactWindowOpen);
    }

    [Fact]
    public void CompactClosedWhileMainOpen_DoesNotExit()
    {
        var tracker = new WindowLifecycleTracker();
        int exitCount = 0;
        tracker.AllWindowsClosed += () => exitCount++;

        tracker.MainWindowOpened();
        tracker.CompactWindowOpened();
        tracker.CompactWindowClosed();

        Assert.Equal(0, exitCount);
        Assert.True(tracker.IsMainWindowOpen);
    }

    [Fact]
    public void LastWindowClosed_TriggersExitOnce()
    {
        var tracker = new WindowLifecycleTracker();
        int exitCount = 0;
        tracker.AllWindowsClosed += () => exitCount++;

        tracker.MainWindowOpened();
        tracker.CompactWindowOpened();
        tracker.MainWindowClosed();
        tracker.CompactWindowClosed();

        Assert.Equal(1, exitCount);
    }

    [Fact]
    public void WindowsCanReopenAfterClose()
    {
        var tracker = new WindowLifecycleTracker();
        int exitCount = 0;
        tracker.AllWindowsClosed += () => exitCount++;

        tracker.MainWindowOpened();
        tracker.CompactWindowOpened();
        tracker.CompactWindowClosed();
        Assert.Equal(0, exitCount);

        // 主窗口仍打开，紧凑窗口可以再次打开。
        tracker.CompactWindowOpened();
        tracker.CompactWindowClosed();
        tracker.MainWindowClosed();

        Assert.Equal(1, exitCount);
        Assert.False(tracker.IsMainWindowOpen);
        Assert.False(tracker.IsCompactWindowOpen);
    }
}
