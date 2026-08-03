namespace ApiMonitor.Services;

/// <summary>
/// 通知区域图标服务：管理托盘生命周期（添加/删除/Explorer 恢复）、
/// 鼠标回调路由、Tooltip 更新与原生菜单命令转发。
/// 不得从回调中同步等待网络请求。
/// </summary>
public interface ITrayIconService
{
    /// <summary>托盘图标当前是否已添加到通知区域。</summary>
    bool IsActive { get; }

    /// <summary>
    /// 初始化并添加图标。幂等：重复调用不生成第二个图标；
    /// 失败时记录错误并返回 false，不导致应用崩溃。
    /// </summary>
    bool Initialize();

    /// <summary>从状态提供者刷新 Tooltip（余额/阈值/查询状态变化时调用）。</summary>
    void UpdateTooltip();

    /// <summary>显式删除图标并释放原生资源。幂等，最多执行一次。</summary>
    void Shutdown();
}
