namespace ApiMonitor.Services;

/// <summary>
/// 主窗口的最小控制器抽象（真实实现包装 WinUI Window / AppWindow），
/// 使关闭到托盘、隐藏与恢复逻辑可单元测试。
/// </summary>
public interface IMainWindowController
{
    /// <summary>主窗口当前是否可见（含最小化状态）。</summary>
    bool IsVisible { get; }

    /// <summary>显示并激活主窗口（最小化时恢复）。</summary>
    void Show();

    /// <summary>隐藏到通知区域（AppWindow.Hide，不销毁窗口对象）。</summary>
    void Hide();

    /// <summary>真正关闭主窗口（显式退出流程中使用）。</summary>
    void Close();

    /// <summary>允许下一次关闭请求真正关闭窗口（退出流程放行）。</summary>
    void AllowClose();
}
