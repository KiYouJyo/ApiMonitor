using ApiMonitor.Models;
using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 托盘菜单状态测试（需求：无账户时刷新项禁用、刷新中禁用、
/// 自动刷新/低余额状态文本、登录启动勾选、菜单分组完整）。
/// </summary>
public sealed class TrayMenuServiceTests
{
    private static readonly TrayMenuService Service = new();

    private static IReadOnlyList<TrayMenuItem> Build(bool hasAccounts, bool refreshingAll = false)
    {
        var context = new TrayMenuContext(
            HasAccounts: hasAccounts,
            IsRefreshingAll: refreshingAll,
            AutoRefreshStatusText: hasAccounts
                ? TrayStatusText.AutoRefreshRunning
                : TrayStatusText.AutoRefreshStopped,
            LowBalanceStatusText: TrayStatusText.LowBalanceNormal,
            StartWithWindowsChecked: false,
            StartWithWindowsEnabled: true);
        return Service.BuildMenu(context);
    }

    [Fact]
    public void NoAccounts_RefreshAllDisabled()
    {
        var items = Build(hasAccounts: false);

        var refreshAll = items.First(i => i.CommandId == (uint)TrayCommand.RefreshAll);
        Assert.False(refreshAll.IsEnabled);
    }

    [Fact]
    public void HasAccounts_RefreshAllEnabled()
    {
        var items = Build(hasAccounts: true);

        var refreshAll = items.First(i => i.CommandId == (uint)TrayCommand.RefreshAll);
        Assert.True(refreshAll.IsEnabled);
    }

    [Fact]
    public void RefreshingAll_RefreshAllDisabled()
    {
        var items = Build(hasAccounts: true, refreshingAll: true);

        var refreshAll = items.First(i => i.CommandId == (uint)TrayCommand.RefreshAll);
        Assert.False(refreshAll.IsEnabled);
    }

    [Fact]
    public void Menu_ContainsRequiredSectionsAndCommands()
    {
        var items = Build(hasAccounts: true);

        // 必需命令都在。
        uint[] required =
        {
            (uint)TrayCommand.OpenMainWindow,
            (uint)TrayCommand.OpenFloatingWindow,
            (uint)TrayCommand.RefreshAll,
            (uint)TrayCommand.ToggleStartWithWindows,
            (uint)TrayCommand.ExitApplication,
        };
        foreach (uint id in required)
        {
            Assert.Contains(items, i => i.CommandId == id);
        }

        // 打开 ApiMonitor 是默认项（醒目位置）。
        var openMain = items.First(i => i.CommandId == (uint)TrayCommand.OpenMainWindow);
        Assert.True(openMain.IsDefault);

        // 状态项禁用（只读文本）。
        Assert.Contains(items, i => !i.IsEnabled && i.Text == TrayStatusText.AutoRefreshRunning);
        Assert.Contains(items, i => !i.IsEnabled && i.Text == TrayStatusText.LowBalanceNormal);

        // 三个分隔符分组。
        Assert.Equal(3, items.Count(i => i.IsSeparator));
    }

    [Fact]
    public void StartWithWindows_CheckedStateReflectsContext()
    {
        var context = new TrayMenuContext(
            HasAccounts: true,
            IsRefreshingAll: false,
            AutoRefreshStatusText: TrayStatusText.AutoRefreshRunning,
            LowBalanceStatusText: TrayStatusText.LowBalanceNormal,
            StartWithWindowsChecked: true,
            StartWithWindowsEnabled: true);
        var items = Service.BuildMenu(context);

        var toggle = items.First(i => i.CommandId == (uint)TrayCommand.ToggleStartWithWindows);
        Assert.True(toggle.IsChecked);
    }

    [Fact]
    public void StartWithWindows_DisabledByPolicy_IsDisabled()
    {
        var context = new TrayMenuContext(
            HasAccounts: true,
            IsRefreshingAll: false,
            AutoRefreshStatusText: TrayStatusText.AutoRefreshRunning,
            LowBalanceStatusText: TrayStatusText.LowBalanceNormal,
            StartWithWindowsChecked: false,
            StartWithWindowsEnabled: false);
        var items = Service.BuildMenu(context);

        var toggle = items.First(i => i.CommandId == (uint)TrayCommand.ToggleStartWithWindows);
        Assert.False(toggle.IsEnabled);
    }
}
