using Microsoft.UI.Xaml;

namespace ApiMonitor.Services;

/// <summary>
/// 窗口生命周期管理：登记主窗口与悬浮窗，最后一个窗口关闭时通知应用退出。
/// 页面不得自行维护全局静态 Window 引用。
/// </summary>
public interface IWindowManager
{
    event Action? AllWindowsClosed;

    /// <summary>主窗口是否已登记（健康检查/诊断只读状态）。</summary>
    bool IsMainWindowOpen { get; }

    /// <summary>是否有悬浮窗处于打开状态。</summary>
    bool IsFloatingWindowOpen { get; }

    void RegisterMainWindow(Window window);

    void RegisterFloatingWindow(Window window);
}
