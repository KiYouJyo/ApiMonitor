namespace ApiMonitor.Services;

/// <summary>
/// 统一退出协调器实现。退出流程幂等（Interlocked 保证最多执行一次）：
/// 停止调度 → 取消在途操作 → 保存设置 → 删除托盘图标 →
/// 关闭紧凑窗口 → 关闭主窗口 → 释放原生句柄 → 退出进程。
/// 不强制 Kill 自身，不使用 Environment.FailFast。
/// </summary>
public sealed class ApplicationExitCoordinator : IApplicationExitCoordinator
{
    private readonly IMonitoringScheduler _scheduler;
    private readonly Func<ITrayIconService> _trayProvider;
    private readonly ICompactWindowService _compactWindowService;
    private readonly ITraySettingsStore _settingsStore;
    private readonly Action _cancelInFlightOperations;
    private readonly Action _closeMainWindow;
    private readonly Action _exitProcess;
    private readonly AppLog _log;
    private int _exitStarted;

    public ApplicationExitCoordinator(
        IMonitoringScheduler scheduler,
        Func<ITrayIconService> trayProvider,
        ICompactWindowService compactWindowService,
        ITraySettingsStore settingsStore,
        Action cancelInFlightOperations,
        Action closeMainWindow,
        Action exitProcess,
        AppLog log)
    {
        _scheduler = scheduler;
        _trayProvider = trayProvider;
        _compactWindowService = compactWindowService;
        _settingsStore = settingsStore;
        _cancelInFlightOperations = cancelInFlightOperations;
        _closeMainWindow = closeMainWindow;
        _exitProcess = exitProcess;
        _log = log;
    }

    public bool IsExiting => Volatile.Read(ref _exitStarted) != 0;

    public void BeginExit()
    {
        if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
        {
            // 幂等：多次点击退出只执行一次完整流程。
            return;
        }

        _ = ExitCoreAsync();
    }

    private async Task ExitCoreAsync()
    {
        try
        {
            _log.Info("应用开始退出。");

            // 1. 停止自动刷新调度；取消在途网络请求与托盘刷新。
            _scheduler.Stop();
            _cancelInFlightOperations();

            // 2. 保存设置（幂等写回当前值）。
            await SaveSettingsAsync().ConfigureAwait(true);

            // 3. 删除通知区域图标（内部取消在途刷新并释放原生句柄）。
            _trayProvider().Shutdown();

            // 4. 关闭紧凑窗口与主窗口。
            _compactWindowService.Shutdown();
            _closeMainWindow();

            _log.Info("应用退出流程完成，进程即将结束。");
        }
        catch (Exception ex)
        {
            _log.Error($"退出流程异常: {ex.GetType().Name}");
        }
        finally
        {
            // 实例键由 AppInstance 在进程退出时自动释放，无需显式注销。
            _exitProcess();
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(CancellationToken.None);
            await _settingsStore.SaveAsync(settings, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Error($"退出时保存设置失败: {ex.GetType().Name}");
        }
    }
}
