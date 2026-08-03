using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// MSIX StartupTask（登录 Windows 时启动）服务抽象。
/// 系统状态是权威来源；本地设置只保存 UI 偏好，不覆盖系统状态。
/// </summary>
public interface IStartupTaskService
{
    /// <summary>最近一次从系统读取的状态缓存（菜单/UI 同步展示用）。</summary>
    StartupTaskStatus? CachedStatus { get; }

    /// <summary>从系统读取 StartupTask 状态并更新缓存。</summary>
    Task<StartupTaskStatus> RefreshStatusAsync(CancellationToken cancellationToken);

    /// <summary>请求启用登录启动；返回操作后的系统状态。</summary>
    Task<StartupTaskStatus> EnableAsync(CancellationToken cancellationToken);

    /// <summary>请求关闭登录启动；返回操作后的系统状态。</summary>
    Task<StartupTaskStatus> DisableAsync(CancellationToken cancellationToken);
}
