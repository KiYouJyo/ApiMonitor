using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Tests.TestDoubles;

/// <summary>内存版托盘设置存储（记录读写）。</summary>
public sealed class FakeTraySettingsStore : ITraySettingsStore
{
    public TraySettings Settings { get; set; } = new();

    public bool ThrowOnLoad { get; set; }

    public int SaveCalls { get; private set; }

    public Task<TraySettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (ThrowOnLoad)
        {
            throw new InvalidOperationException("模拟读取失败");
        }

        return Task.FromResult(Settings);
    }

    public Task SaveAsync(TraySettings settings, CancellationToken cancellationToken)
    {
        Settings = settings;
        SaveCalls++;
        return Task.CompletedTask;
    }
}

/// <summary>内存版登录启动服务（记录开关调用）。</summary>
public sealed class FakeStartupTaskService : IStartupTaskService
{
    public StartupTaskStatus? CachedStatus { get; set; } = StartupTaskStatus.Disabled;

    public StartupTaskStatus RefreshResult { get; set; } = StartupTaskStatus.Disabled;

    public StartupTaskStatus EnableResult { get; set; } = StartupTaskStatus.Enabled;

    public StartupTaskStatus DisableResult { get; set; } = StartupTaskStatus.Disabled;

    public int RefreshCalls { get; private set; }

    public int EnableCalls { get; private set; }

    public int DisableCalls { get; private set; }

    public Task<StartupTaskStatus> RefreshStatusAsync(CancellationToken cancellationToken)
    {
        RefreshCalls++;
        CachedStatus = RefreshResult;
        return Task.FromResult(RefreshResult);
    }

    public Task<StartupTaskStatus> EnableAsync(CancellationToken cancellationToken)
    {
        EnableCalls++;
        CachedStatus = EnableResult;
        return Task.FromResult(EnableResult);
    }

    public Task<StartupTaskStatus> DisableAsync(CancellationToken cancellationToken)
    {
        DisableCalls++;
        CachedStatus = DisableResult;
        return Task.FromResult(DisableResult);
    }
}

/// <summary>记录 BeginExit 调用的假退出协调器。</summary>
public sealed class FakeExitCoordinator : IApplicationExitCoordinator
{
    public bool IsExiting { get; set; }

    public int BeginExitCalls { get; private set; }

    public void BeginExit()
    {
        BeginExitCalls++;
        IsExiting = true;
    }
}

/// <summary>记录打开/关闭调用的假紧凑窗口服务。</summary>
public sealed class FakeCompactWindowService : ICompactWindowService
{
    public int OpenOrActivateCalls { get; private set; }

    public int ShutdownCalls { get; private set; }

    public bool IsWindowOpen { get; set; }

    public void OpenOrActivate() => OpenOrActivateCalls++;

    public void CloseWindow()
    {
        IsWindowOpen = false;
    }

    public void Shutdown()
    {
        ShutdownCalls++;
        IsWindowOpen = false;
    }
}

/// <summary>记录 Start/Stop 调用的假调度器。</summary>
public sealed class FakeMonitoringScheduler : IMonitoringScheduler
{
    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public void Start(CancellationToken applicationToken) => StartCalls++;

    public void Stop() => StopCalls++;

    public Task TickAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>记录显示/隐藏/关闭调用的假主窗口控制器。</summary>
public sealed class FakeMainWindowController : IMainWindowController
{
    public bool IsVisible { get; private set; }

    public int ShowCalls { get; private set; }

    public int HideCalls { get; private set; }

    public int CloseCalls { get; private set; }

    public bool AllowCloseCalled { get; private set; }

    public void Show()
    {
        IsVisible = true;
        ShowCalls++;
    }

    public void Hide()
    {
        IsVisible = false;
        HideCalls++;
    }

    public void Close()
    {
        IsVisible = false;
        CloseCalls++;
    }

    public void AllowClose() => AllowCloseCalled = true;
}

/// <summary>返回固定状态的假托盘状态提供者。</summary>
public sealed class FakeTrayStatusProvider : ITrayStatusProvider
{
    public TrayStatusSnapshot Status { get; set; } = new(
        TrayStatusText.TooltipFor(0, hasAnySnapshot: false, isRefreshing: false, hasRecentFailure: false),
        IsRefreshing: false,
        HasRecentFailure: false,
        HasAnySnapshot: false,
        LowBalanceRuleCount: 0);

    public Task<TrayStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Status);
}

/// <summary>记录 Shutdown 调用的假托盘图标服务。</summary>
public sealed class FakeTrayIconService : ITrayIconService
{
    public bool IsActive { get; set; }

    public int InitializeCalls { get; private set; }

    public int ShutdownCalls { get; private set; }

    public bool Initialize()
    {
        InitializeCalls++;
        IsActive = true;
        return true;
    }

    public void UpdateTooltip()
    {
    }

    public void Shutdown()
    {
        ShutdownCalls++;
        IsActive = false;
    }
}
