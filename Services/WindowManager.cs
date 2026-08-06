using Microsoft.UI.Xaml;

namespace ApiMonitor.Services;

/// <summary>
/// 基于 WindowLifecycleTracker 的窗口登记实现。窗口关闭事件只更新计数器，
/// 不直接持有静态 Window 引用。
/// </summary>
public sealed class WindowManager : IWindowManager
{
    private readonly WindowLifecycleTracker _tracker = new();

    public event Action? AllWindowsClosed
    {
        add => _tracker.AllWindowsClosed += value;
        remove => _tracker.AllWindowsClosed -= value;
    }

    public bool IsMainWindowOpen => _tracker.IsMainWindowOpen;

    public bool IsFloatingWindowOpen => _tracker.IsFloatingWindowOpen;

    public void RegisterMainWindow(Window window)
    {
        _tracker.MainWindowOpened();
        window.Closed += (_, _) => _tracker.MainWindowClosed();
    }

    public void RegisterFloatingWindow(Window window)
    {
        _tracker.FloatingWindowOpened();
        window.Closed += (_, _) => _tracker.FloatingWindowClosed();
    }
}
