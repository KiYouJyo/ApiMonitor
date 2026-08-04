namespace ApiMonitor.Services;

/// <summary>托盘菜单命令 ID（与 NativeMethods.MENU_ID_BASE 对齐）。</summary>
public enum TrayCommand : uint
{
    OpenMainWindow = 0x1000,
    OpenFloatingWindow = 0x1001,
    CloseFloatingWindow = 0x1002,
    RefreshAll = 0x1003,
    ToggleStartWithWindows = 0x1004,
    ExitApplication = 0x1005,
}

/// <summary>构建托盘菜单所需的上下文（每次弹出前重新计算）。</summary>
public sealed record TrayMenuContext(
    bool HasAccounts,
    bool IsFloatingWindowOpen,
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

/// <summary>
/// 默认托盘菜单布局：打开/悬浮窗、刷新与状态、登录启动、退出。
/// v0.6.0：文本通过 IAppStrings 按当前 UI 语言取；未注入时回退内置中文。
/// </summary>
public sealed class TrayMenuService : ITrayMenuService
{
    private readonly IAppStrings? _strings;

    public TrayMenuService(IAppStrings? strings = null)
    {
        _strings = strings;
    }

    public IReadOnlyList<TrayMenuItem> BuildMenu(TrayMenuContext context)
    {
        var items = new List<TrayMenuItem>
        {
            new(T("Tray.OpenMainWindow", "打开 ApiMonitor"), (uint)TrayCommand.OpenMainWindow, IsDefault: true),
            new(T("Tray.OpenFloatingWindow", "打开悬浮窗"), (uint)TrayCommand.OpenFloatingWindow),
            new(
                T("Tray.CloseFloatingWindow", "关闭悬浮窗"),
                (uint)TrayCommand.CloseFloatingWindow,
                IsEnabled: context.IsFloatingWindowOpen),
            new(null, 0, IsSeparator: true),
            new(
                T("Tray.RefreshAll", "刷新全部账户"),
                (uint)TrayCommand.RefreshAll,
                IsEnabled: context.HasAccounts && !context.IsRefreshingAll),
            new(context.AutoRefreshStatusText, 0, IsEnabled: false),
            new(context.LowBalanceStatusText, 0, IsEnabled: false),
            new(null, 0, IsSeparator: true),
            new(
                T("Tray.StartWithWindows", "登录时启动"),
                (uint)TrayCommand.ToggleStartWithWindows,
                IsEnabled: context.StartWithWindowsEnabled,
                IsChecked: context.StartWithWindowsChecked),
            new(null, 0, IsSeparator: true),
            new(T("Tray.ExitApplication", "退出 ApiMonitor"), (uint)TrayCommand.ExitApplication),
        };

        return items;
    }

    private string T(string key, string fallback) =>
        _strings is null ? fallback : _strings.Get(key);
}
