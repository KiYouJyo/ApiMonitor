namespace ApiMonitor.Services;

/// <summary>
/// 统一应用退出协调器。退出流程幂等，最多执行一次：
/// 标记退出 → 停止调度与在途操作 → 保存设置 → 删除托盘图标 →
/// 关闭悬浮窗/主窗口/隐藏消息窗口 → 释放原生句柄 → 退出进程。
/// </summary>
public interface IApplicationExitCoordinator
{
    /// <summary>是否已进入退出流程（用于关闭窗口时放行真正关闭等判断）。</summary>
    bool IsExiting { get; }

    /// <summary>开始统一退出。多次调用安全（幂等）。</summary>
    void BeginExit();
}
