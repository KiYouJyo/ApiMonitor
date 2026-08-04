namespace ApiMonitor.Services;

/// <summary>
/// 悬浮余额窗服务：整个应用只维护一个悬浮窗实例；
/// 重复打开时激活/恢复已有窗口，关闭后可再次创建。
/// Show(accountId) 用于把指定账户设为悬浮窗账户并显示/切换。
/// </summary>
public interface IFloatingWindowService
{
    bool IsWindowOpen { get; }

    /// <summary>打开悬浮窗；accountId 非空时同时切换为该账户。</summary>
    void Show(string? accountId = null);

    void CloseWindow();

    void Shutdown();
}
