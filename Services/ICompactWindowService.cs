namespace ApiMonitor.Services;

/// <summary>
/// 紧凑窗口服务：整个应用只维护一个紧凑窗口实例；
/// 重复打开时激活/恢复已有窗口，关闭后可再次创建。
/// </summary>
public interface ICompactWindowService
{
    bool IsWindowOpen { get; }

    void OpenOrActivate();

    void CloseWindow();

    void Shutdown();
}
