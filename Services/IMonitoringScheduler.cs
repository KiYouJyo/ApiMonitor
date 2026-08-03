using ApiBalanceMonitor.Models;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 应用运行期间的自动刷新调度服务。单一调度循环检查到期账户，
/// 不按账户创建线程；应用退出时取消全部调度。
/// </summary>
public interface IMonitoringScheduler
{
    void Start(CancellationToken applicationToken);

    void Stop();

    /// <summary>执行一次到期检查（供调度循环与测试调用）。</summary>
    Task TickAsync(CancellationToken cancellationToken);
}
