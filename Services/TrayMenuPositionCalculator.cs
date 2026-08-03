namespace ApiMonitor.Services;

/// <summary>托盘图标屏幕矩形（Win32 屏幕坐标，物理像素）。</summary>
public readonly record struct TrayScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public int CenterX => Left + Width / 2;

    public int CenterY => Top + Height / 2;
}

/// <summary>TrackPopupMenuEx 的最终定位：屏幕坐标 + 对齐标志。</summary>
public readonly record struct TrayMenuPlacement(int X, int Y, uint AlignFlags);

/// <summary>
/// 托盘右键菜单位置计算（纯逻辑，可测试）：
/// 锚点优先级 GetCursorPos → wParam 坐标（NOTIFYICON_VERSION_4）→ 图标矩形；
/// 方向按锚点所在显示器工作区的半区选择（底部→向上、顶部→向下、右半→向左、左半→向右），
/// 支持底部/顶部/左侧/右侧任务栏、多显示器、负坐标显示器；坐标全程为物理像素，不做任何缩放。
/// 不返回未经验证的 (0,0)：所有候选无效时返回 null，由调用方放弃本次菜单显示。
/// </summary>
public static class TrayMenuPositionCalculator
{
    /// <summary>
    /// 解析最终菜单位置。
    /// </summary>
    /// <param name="cursor">GetCursorPos 成功得到的鼠标屏幕坐标（可能为 null）。</param>
    /// <param name="wParamPoint">NOTIFYICON_VERSION_4 下 WM_CONTEXTMENU 的 wParam 坐标（可能为 null）。</param>
    /// <param name="iconRect">Shell_NotifyIconGetRect 得到的图标矩形（可能为 null）。</param>
    /// <param name="getWorkArea">按屏幕坐标返回所在显示器的工作区（MonitorFromPoint + GetMonitorInfo；无效坐标返回 null）。</param>
    /// <returns>有效位置；全部候选无效时返回 null（调用方应放弃显示，绝不使用 (0,0)）。</returns>
    public static TrayMenuPlacement? Resolve(
        TrayScreenPoint? cursor,
        TrayScreenPoint? wParamPoint,
        TrayScreenRect? iconRect,
        Func<TrayScreenPoint, TrayScreenRect?> getWorkArea)
    {
        if (cursor is { } c && getWorkArea(c) is { } workAreaCursor)
        {
            return Build(c, iconRect: null, workAreaCursor);
        }

        if (wParamPoint is { } w && getWorkArea(w) is { } workAreaWParam)
        {
            return Build(w, iconRect: null, workAreaWParam);
        }

        if (iconRect is { } rect)
        {
            var center = new TrayScreenPoint(rect.CenterX, rect.CenterY);
            if (getWorkArea(center) is { } workAreaIcon)
            {
                return Build(center, rect, workAreaIcon);
            }
        }

        return null;
    }

    private static TrayMenuPlacement Build(
        TrayScreenPoint anchor,
        TrayScreenRect? iconRect,
        TrayScreenRect workArea)
    {
        // 锚点相对工作区的半区决定展开方向。
        bool anchorInBottomHalf = anchor.Y >= workArea.Top + workArea.Height / 2;
        bool anchorInRightHalf = anchor.X >= workArea.Left + workArea.Width / 2;

        uint align = anchorInBottomHalf
            ? NativeMethods.TPM_BOTTOMALIGN  // 图标/光标在工作区下半部：菜单向上展开
            : NativeMethods.TPM_TOPALIGN;    // 上半部：向下展开

        align |= anchorInRightHalf
            ? NativeMethods.TPM_RIGHTALIGN   // 右半部：菜单向右对齐（向左展开）
            : NativeMethods.TPM_LEFTALIGN;   // 左半部：向左对齐（向右展开）

        // 锚点：鼠标触发用光标坐标；图标触发用图标边缘（紧邻图标）。
        int x;
        int y;
        if (iconRect is { } rect)
        {
            x = (align & NativeMethods.TPM_RIGHTALIGN) != 0 ? rect.Left : rect.Right;
            y = (align & NativeMethods.TPM_BOTTOMALIGN) != 0 ? rect.Top : rect.Bottom;
        }
        else
        {
            x = anchor.X;
            y = anchor.Y;
        }

        return new TrayMenuPlacement(x, y, align);
    }
}
