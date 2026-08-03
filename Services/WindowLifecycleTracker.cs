namespace ApiMonitor.Services;

/// <summary>
/// 纯逻辑的窗口生命周期跟踪：主窗口与紧凑窗口都关闭时触发应用退出信号。
/// 不含任何 WinUI 依赖，便于单元测试。
/// </summary>
public sealed class WindowLifecycleTracker
{
    public bool IsMainWindowOpen { get; private set; }

    public bool IsCompactWindowOpen { get; private set; }

    public event Action? AllWindowsClosed;

    public void MainWindowOpened() => IsMainWindowOpen = true;

    public void MainWindowClosed()
    {
        IsMainWindowOpen = false;
        RaiseIfEmpty();
    }

    public void CompactWindowOpened() => IsCompactWindowOpen = true;

    public void CompactWindowClosed()
    {
        IsCompactWindowOpen = false;
        RaiseIfEmpty();
    }

    private void RaiseIfEmpty()
    {
        if (!IsMainWindowOpen && !IsCompactWindowOpen)
        {
            AllWindowsClosed?.Invoke();
        }
    }
}
