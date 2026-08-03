using ApiBalanceMonitor.Models;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 基于单一 PeriodicTimer 的调度循环：每 30 秒检查一次到期账户并触发自动刷新。
/// 到期账户逐个错峰发起；查询失败不影响其他账户。
/// </summary>
public sealed class MonitoringScheduler : IMonitoringScheduler
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaggerDelay = TimeSpan.FromMilliseconds(500);

    private readonly IAccountManager _accountManager;
    private readonly TimeProvider _time;
    private readonly AppLog _log;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public MonitoringScheduler(IAccountManager accountManager, TimeProvider time, AppLog log)
    {
        _accountManager = accountManager;
        _time = time;
        _log = log;
    }

    public void Start(CancellationToken applicationToken)
    {
        lock (_gate)
        {
            if (_loop is not null)
            {
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(applicationToken);
            _loop = LoopAsync(_cts.Token);
            _log.Info("监控调度器已启动。");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _loop = null;
            _cts?.Dispose();
            _cts = null;
            _log.Info("监控调度器已停止。");
        }
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var due = await _accountManager.GetAutoRefreshDueAccountIdsAsync(
            _time.GetUtcNow(),
            cancellationToken);

        foreach (var accountId in due)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _ = RefreshSafelyAsync(accountId, cancellationToken);
            try
            {
                await Task.Delay(StaggerDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 启动后立即执行一次到期检查（含长时间暂停后的补一次刷新）。
            await TickAsync(cancellationToken);

            using var timer = new PeriodicTimer(TickInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await TickAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"调度循环异常: {ex.GetType().Name}");
        }
    }

    private async Task RefreshSafelyAsync(string accountId, CancellationToken cancellationToken)
    {
        try
        {
            await _accountManager.RefreshAccountAsync(
                accountId,
                BalanceQuerySource.Automatic,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"自动刷新异常: {ex.GetType().Name}");
        }
    }
}
