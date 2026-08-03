using ApiMonitor.Models;
using Windows.ApplicationModel;

namespace ApiMonitor.Services;

/// <summary>
/// MSIX StartupTask 登录启动实现。系统 StartupTask 状态是权威来源：
/// 只在用户明确操作时调用 RequestEnableAsync/Disable，不写 Run 键、
/// 不写 Startup 文件夹、不创建计划任务。
/// 未打包运行（无包身份）时返回 Unknown。
/// </summary>
public sealed class StartupTaskService : IStartupTaskService
{
    internal const string TaskId = "ApiMonitorStartup";

    private readonly AppLog? _log;
    private StartupTaskStatus? _cached;

    public StartupTaskService(AppLog? log = null)
    {
        _log = log;
    }

    public StartupTaskStatus? CachedStatus => _cached;

    public async Task<StartupTaskStatus> RefreshStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartupTaskStatus status = await GetStatusAsync(cancellationToken);
        _cached = status;
        return status;
    }

    public async Task<StartupTaskStatus> EnableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var task = await GetStartupTaskAsync();
            if (task is null)
            {
                _cached = StartupTaskStatus.Unknown;
                return StartupTaskStatus.Unknown;
            }

            var state = await task.RequestEnableAsync();
            var mapped = Map(state);
            _cached = mapped;
            return mapped;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.Error($"启用登录启动失败: {ex.GetType().Name}");
            _cached = StartupTaskStatus.Unknown;
            return StartupTaskStatus.Unknown;
        }
    }

    public async Task<StartupTaskStatus> DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var task = await GetStartupTaskAsync();
            if (task is null)
            {
                _cached = StartupTaskStatus.Unknown;
                return StartupTaskStatus.Unknown;
            }

            task.Disable();
            var mapped = Map(task.State);
            _cached = mapped;
            return mapped;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.Error($"关闭登录启动失败: {ex.GetType().Name}");
            _cached = StartupTaskStatus.Unknown;
            return StartupTaskStatus.Unknown;
        }
    }

    private async Task<StartupTaskStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var task = await GetStartupTaskAsync();
            return task is null ? StartupTaskStatus.Unknown : Map(task.State);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 未打包/无包身份时抛异常：视为 Unknown，不崩溃。
            _log?.Error($"读取登录启动状态失败: {ex.GetType().Name}");
            return StartupTaskStatus.Unknown;
        }
    }

    private static async Task<StartupTask?> GetStartupTaskAsync()
    {
        // StartupTask.GetAsync 需要包身份；未打包时抛异常，由调用方捕获。
        return await StartupTask.GetAsync(TaskId);
    }

    private static StartupTaskStatus Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => StartupTaskStatus.Disabled,
        StartupTaskState.DisabledByUser => StartupTaskStatus.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupTaskStatus.DisabledByPolicy,
        StartupTaskState.Enabled => StartupTaskStatus.Enabled,
        StartupTaskState.EnabledByPolicy => StartupTaskStatus.EnabledByPolicy,
        _ => StartupTaskStatus.Unknown,
    };
}
