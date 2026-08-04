namespace ApiMonitor.Services;

/// <summary>
/// 纯逻辑的窗口生命周期跟踪：主窗口与悬浮窗都关闭时触发应用退出信号。
/// 不含任何 WinUI 依赖，便于单元测试。
/// </summary>
public sealed class WindowLifecycleTracker
{
    public bool IsMainWindowOpen { get; private set; }

    public bool IsFloatingWindowOpen { get; private set; }

    public event Action? AllWindowsClosed;

    public void MainWindowOpened() => IsMainWindowOpen = true;

    public void MainWindowClosed()
    {
        IsMainWindowOpen = false;
        RaiseIfEmpty();
    }

    public void FloatingWindowOpened() => IsFloatingWindowOpen = true;

    public void FloatingWindowClosed()
    {
        IsFloatingWindowOpen = false;
        RaiseIfEmpty();
    }

    private void RaiseIfEmpty()
    {
        if (!IsMainWindowOpen && !IsFloatingWindowOpen)
        {
            AllWindowsClosed?.Invoke();
        }
    }
}
