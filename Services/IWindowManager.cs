using Microsoft.UI.Xaml;

namespace ApiMonitor.Services;

/// <summary>
/// 窗口生命周期管理：登记主窗口与紧凑窗口，最后一个窗口关闭时通知应用退出。
/// 页面不得自行维护全局静态 Window 引用。
/// </summary>
public interface IWindowManager
{
    event Action? AllWindowsClosed;

    void RegisterMainWindow(Window window);

    void RegisterCompactWindow(Window window);
}
