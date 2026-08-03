namespace ApiMonitor.Services;

/// <summary>托盘菜单命令 ID（与 NativeMethods.MENU_ID_BASE 对齐）。</summary>
public enum TrayCommand : uint
{
    OpenMainWindow = 0x1000,
    OpenCompactWindow = 0x1001,
    RefreshAll = 0x1002,
    ToggleStartWithWindows = 0x1003,
    ExitApplication = 0x1004,
}

/// <summary>构建托盘菜单所需的上下文（每次弹出前重新计算）。</summary>
public sealed record TrayMenuContext(
    bool HasAccounts,
    bool IsRefreshingAll,
    string AutoRefreshStatusText,
    string LowBalanceStatusText,
    bool StartWithWindowsChecked,
    bool StartWithWindowsEnabled);

/// <summary>托盘菜单构建服务：把上下文映射为原生菜单项描述（纯数据，便于测试）。</summary>
public interface ITrayMenuService
{
    IReadOnlyList<TrayMenuItem> BuildMenu(TrayMenuContext context);
}

/// <summary>默认托盘菜单布局：打开/紧凑、刷新与状态、登录启动、退出。</summary>
public sealed class TrayMenuService : ITrayMenuService
{
    public IReadOnlyList<TrayMenuItem> BuildMenu(TrayMenuContext context)
    {
        var items = new List<TrayMenuItem>
        {
            new("打开 ApiMonitor", (uint)TrayCommand.OpenMainWindow, IsDefault: true),
            new("打开紧凑窗口", (uint)TrayCommand.OpenCompactWindow),
            new(null, 0, IsSeparator: true),
            new(
                "刷新全部账户",
                (uint)TrayCommand.RefreshAll,
                IsEnabled: context.HasAccounts && !context.IsRefreshingAll),
            new(context.AutoRefreshStatusText, 0, IsEnabled: false),
            new(context.LowBalanceStatusText, 0, IsEnabled: false),
            new(null, 0, IsSeparator: true),
            new(
                "登录时启动",
                (uint)TrayCommand.ToggleStartWithWindows,
                IsEnabled: context.StartWithWindowsEnabled,
                IsChecked: context.StartWithWindowsChecked),
            new(null, 0, IsSeparator: true),
            new("退出 ApiMonitor", (uint)TrayCommand.ExitApplication),
        };

        return items;
    }
}
