using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 托盘生命周期与命令路由测试（需求：初始化只添加一个图标、重复初始化不重复添加、
/// 显式退出删除图标、初始化失败不崩溃、TaskbarCreated 重加且不重复注册、
/// 退出幂等、退出后回调忽略、左键打开窗口、双击不冲突、菜单命令路由）。
/// </summary>
public sealed class TrayIconServiceTests
{
    private sealed class Harness
    {
        public FakeTrayNativeHost Host { get; } = new();

        public FakeAccountManager AccountManager { get; } = new();

        public FakeCompactWindowService Compact { get; } = new();

        public FakeStartupTaskService StartupTask { get; } = new();

        public FakeTraySettingsStore SettingsStore { get; } = new();

        public FakeExitCoordinator Exit { get; } = new();

        public FakeTrayStatusProvider StatusProvider { get; } = new();

        public int ShowMainWindowCalls { get; private set; }

        public int ExitApplicationCalls { get; private set; }

        public AppLog Log { get; } = new(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"abm-tray-log-{Guid.NewGuid():N}"));

        public TrayIconService CreateSut()
        {
            return new TrayIconService(
                Host,
                StatusProvider,
                new TrayMenuService(),
                AccountManager,
                Compact,
                StartupTask,
                SettingsStore,
                () => ExitApplicationCalls++,
                Log,
                () => ShowMainWindowCalls++);
        }
    }

    [Fact]
    public void Initialize_AddsExactlyOneIcon()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();

        bool first = sut.Initialize();
        bool second = sut.Initialize();

        Assert.True(first);
        Assert.True(second);
        Assert.True(sut.IsActive);
        Assert.Equal(1, harness.Host.AddIconCalls);
    }

    [Fact]
    public void Initialize_Failure_DoesNotCrashAndIsNotActive()
    {
        var harness = new Harness();
        harness.Host.AddIconResult = false;
        var sut = harness.CreateSut();

        bool result = sut.Initialize();

        Assert.False(result);
        Assert.False(sut.IsActive);
        // 失败后仍可再次尝试初始化。
        harness.Host.AddIconResult = true;
        Assert.True(sut.Initialize());
        Assert.Equal(2, harness.Host.AddIconCalls);
    }

    [Fact]
    public void Shutdown_DeletesIconExactlyOnce()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();
        sut.Initialize();

        sut.Shutdown();
        sut.Shutdown();

        Assert.Equal(1, harness.Host.DeleteIconCalls);
        Assert.True(harness.Host.Disposed);
        Assert.False(sut.IsActive);
    }

    [Fact]
    public void Shutdown_WithoutInitialize_IsSafe()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();

        sut.Shutdown();

        Assert.Equal(0, harness.Host.DeleteIconCalls);
    }

    [Fact]
    public void TaskbarCreated_ReAddsIconWithoutDuplicateRegistration()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();
        sut.Initialize();
        Assert.Equal(1, harness.Host.AddIconCalls);

        harness.Host.RaiseTaskbarCreated();
        Assert.Equal(2, harness.Host.AddIconCalls);

        harness.Host.RaiseTaskbarCreated();
        Assert.Equal(3, harness.Host.AddIconCalls);
    }

    [Fact]
    public void TaskbarCreated_AfterShutdown_IsIgnored()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();
        sut.Initialize();

        sut.Shutdown();
        int callsAfterShutdown = harness.Host.AddIconCalls;
        harness.Host.RaiseTaskbarCreated();

        Assert.Equal(callsAfterShutdown, harness.Host.AddIconCalls);
    }

    [Fact]
    public async Task LeftClick_ShowsMainWindow()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();
        sut.Initialize();

        harness.Host.RaiseLeftClick();
        await Task.Delay(400); // 等待单击去抖延迟。

        Assert.Equal(1, harness.ShowMainWindowCalls);
    }

    [Fact]
    public async Task LeftDoubleClick_OpensWindowOnlyOnce()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();
        sut.Initialize();

        harness.Host.RaiseLeftClick();
        harness.Host.RaiseLeftDoubleClick();
        await Task.Delay(400);

        Assert.Equal(1, harness.ShowMainWindowCalls);
    }

    [Fact]
    public void LeftClick_AfterShutdown_IsIgnored()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();
        sut.Initialize();
        sut.Shutdown();

        harness.Host.RaiseLeftClick();
        harness.Host.RaiseLeftDoubleClick();

        Assert.Equal(0, harness.ShowMainWindowCalls);
    }

    [Fact]
    public void ContextMenu_OpenMainWindow_RoutesToShow()
    {
        var harness = new Harness();
        harness.Host.ShowContextMenuResult = (uint)TrayCommand.OpenMainWindow;
        var sut = harness.CreateSut();
        sut.Initialize();

        harness.Host.RaiseContextMenu(new TrayScreenPoint(10, 20));

        Assert.Equal(1, harness.ShowMainWindowCalls);
    }

    [Fact]
    public void ContextMenu_OpenCompactWindow_KeepsSingleInstance()
    {
        var harness = new Harness();
        harness.Host.ShowContextMenuResult = (uint)TrayCommand.OpenCompactWindow;
        var sut = harness.CreateSut();
        sut.Initialize();

        harness.Host.RaiseContextMenu(new TrayScreenPoint(0, 0));

        Assert.Equal(1, harness.Compact.OpenOrActivateCalls);
    }

    [Fact]
    public void ContextMenu_RefreshAll_ReusesAccountManager()
    {
        var harness = new Harness();
        harness.Host.ShowContextMenuResult = (uint)TrayCommand.RefreshAll;
        harness.AccountManager.Accounts.Add(new()
        {
            AccountId = "a1",
            ProviderId = "deepseek",
            DisplayName = "A",
            HasCredential = true,
            CreatedAtUtc = System.DateTimeOffset.UtcNow,
            UpdatedAtUtc = System.DateTimeOffset.UtcNow,
        });
        var sut = harness.CreateSut();
        sut.Initialize();

        harness.Host.RaiseContextMenu(new TrayScreenPoint(0, 0));

        Assert.Equal(1, harness.AccountManager.RefreshAllCalls);
    }

    [Fact]
    public void ContextMenu_ExitApplication_TriggersExit()
    {
        var harness = new Harness();
        harness.Host.ShowContextMenuResult = (uint)TrayCommand.ExitApplication;
        var sut = harness.CreateSut();
        sut.Initialize();

        harness.Host.RaiseContextMenu(new TrayScreenPoint(0, 0));

        Assert.Equal(1, harness.ExitApplicationCalls);
    }

    [Fact]
    public void Menu_RefreshAll_DisabledWhenNoAccounts()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();
        sut.Initialize();

        harness.Host.RaiseContextMenu(new TrayScreenPoint(0, 0));

        var items = harness.Host.MenuItems.Last();
        var refreshAll = items.First(i => i.CommandId == (uint)TrayCommand.RefreshAll);
        Assert.False(refreshAll.IsEnabled);
    }

    [Fact]
    public void Menu_ContainsAllRequiredCommands()
    {
        var harness = new Harness();
        harness.AccountManager.Accounts.Add(new()
        {
            AccountId = "a1",
            ProviderId = "deepseek",
            DisplayName = "A",
            HasCredential = true,
            CreatedAtUtc = System.DateTimeOffset.UtcNow,
            UpdatedAtUtc = System.DateTimeOffset.UtcNow,
        });
        var sut = harness.CreateSut();
        sut.Initialize();

        harness.Host.RaiseContextMenu(new TrayScreenPoint(0, 0));

        var items = harness.Host.MenuItems.Last();
        Assert.Contains(items, i => i.CommandId == (uint)TrayCommand.OpenMainWindow && i.IsDefault);
        Assert.Contains(items, i => i.CommandId == (uint)TrayCommand.OpenCompactWindow);
        Assert.Contains(items, i => i.CommandId == (uint)TrayCommand.RefreshAll && i.IsEnabled);
        Assert.Contains(items, i => i.CommandId == (uint)TrayCommand.ToggleStartWithWindows);
        Assert.Contains(items, i => i.CommandId == (uint)TrayCommand.ExitApplication);
    }

    [Fact]
    public void AccountRefreshCompleted_UpdatesTooltip()
    {
        var harness = new Harness();
        var sut = harness.CreateSut();
        sut.Initialize();
        int tipCallsBefore = harness.Host.UpdateTipCalls;

        harness.AccountManager.RaiseRefreshCompleted(
            "a1",
            Models.BalanceQueryResult.Failure(
                Models.BalanceErrorKind.Unknown,
                "err"),
            Models.BalanceQuerySource.Automatic);

        Assert.True(harness.Host.UpdateTipCalls > tipCallsBefore);
    }

    [Fact]
    public async Task RefreshAllWhileRunning_DoesNotDuplicate()
    {
        var harness = new Harness();
        harness.Host.ShowContextMenuResult = (uint)TrayCommand.RefreshAll;
        harness.AccountManager.RefreshAllGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = harness.CreateSut();
        sut.Initialize();

        harness.Host.RaiseContextMenu(new TrayScreenPoint(0, 0));
        harness.Host.RaiseContextMenu(new TrayScreenPoint(0, 0));

        // 第一次刷新仍在进行时，第二次点击不重复触发。
        Assert.Equal(1, harness.AccountManager.RefreshAllCalls);
        harness.AccountManager.RefreshAllGate.SetResult();
        await Task.Delay(200);
    }
}
