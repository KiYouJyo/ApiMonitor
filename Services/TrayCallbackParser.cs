namespace ApiMonitor.Services;

/// <summary>托盘回调事件类型（NOTIFYICON_VERSION_4 下取自 LOWORD(lParam)）。</summary>
public enum TrayCallbackEventKind
{
    LeftClick,

    LeftDoubleClick,

    ContextMenu,

    Other,
}

/// <summary>
/// 解析后的托盘回调事件（NOTIFYICON_VERSION_4 参数布局）：
/// - LOWORD(lParam) = 事件类型；
/// - HIWORD(lParam) = 图标 ID（NIF_GUID 模式下通常为 0）；
/// - WM_CONTEXTMENU 的屏幕坐标锚点在 wParam（GET_X/Y_LPARAM(wParam)），
///   键盘触发时 wParam 为目标图标左上角；鼠标触发时应优先使用 GetCursorPos。
/// </summary>
public readonly record struct TrayCallbackEvent(
    TrayCallbackEventKind Kind,
    ushort IconId,
    TrayScreenPoint? WParamPoint)
{
    public bool IsContextMenu => Kind == TrayCallbackEventKind.ContextMenu;
}

/// <summary>
/// 把 NOTIFYICON_VERSION_4 的托盘回调参数解析为事件（纯逻辑，可测试）。
/// 注意：不要把整个 lParam 当作事件值——事件类型在 LOWORD(lParam)。
/// </summary>
public static class TrayCallbackParser
{
    /// <summary>从 wParam 提取屏幕坐标（GET_X_LPARAM / GET_Y_LPARAM，带符号 16 位）。</summary>
    public static TrayScreenPoint PointFromWParam(nint wParam)
    {
        long value = (long)wParam;
        int x = (short)(value & 0xFFFF);
        int y = (short)((value >> 16) & 0xFFFF);
        return new TrayScreenPoint(x, y);
    }

    /// <summary>解析回调参数：事件类型取 LOWORD(lParam)，图标 ID 取 HIWORD(lParam)。</summary>
    public static TrayCallbackEvent ParseTrayCallback(nint wParam, nint lParam)
    {
        long raw = (long)lParam;
        ushort iconId = (ushort)((raw >> 16) & 0xFFFF);
        uint kind = (uint)(raw & 0xFFFF);

        var eventKind = kind switch
        {
            NativeMethods.WM_LBUTTONUP => TrayCallbackEventKind.LeftClick,
            NativeMethods.WM_LBUTTONDBLCLK => TrayCallbackEventKind.LeftDoubleClick,
            NativeMethods.WM_CONTEXTMENU => TrayCallbackEventKind.ContextMenu,
            _ => TrayCallbackEventKind.Other,
        };

        TrayScreenPoint? wParamPoint = eventKind == TrayCallbackEventKind.ContextMenu
            ? PointFromWParam(wParam)
            : null;

        return new TrayCallbackEvent(eventKind, iconId, wParamPoint);
    }
}
