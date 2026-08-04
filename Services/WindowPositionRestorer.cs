namespace ApiMonitor.Services;

/// <summary>物理像素矩形（与 Windows.Graphics.RectInt32 布局一致）。</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>显示器工作区信息（多显示器位置恢复的最小抽象）。</summary>
public sealed record DisplayAreaInfo(string DisplayId, PixelRect WorkArea, bool IsPrimary);

/// <summary>提供当前显示器工作区列表（真实实现包装 Microsoft.UI.Windowing.DisplayArea）。</summary>
public interface IDisplayAreaProvider
{
    IReadOnlyList<DisplayAreaInfo> GetAll();

    DisplayAreaInfo GetPrimary();

    /// <summary>返回包含指定窗口区域（按其中心点）的显示器；找不到时返回主显示器。</summary>
    DisplayAreaInfo GetContaining(PixelRect rect);

    DisplayAreaInfo GetByDisplayId(string displayId);
}

/// <summary>
/// 窗口位置/尺寸恢复的纯逻辑：防止窗口恢复到屏幕外、尺寸过大、
/// 显示器被拔出或 DPI/工作区变化导致不可见。
/// </summary>
public static class WindowPositionRestorer
{
    /// <summary>标题栏可见所需的最小可见高度。</summary>
    private const int MinVisibleHeight = 60;

    /// <summary>窗口仍应保留在屏幕内的最小可见宽度。</summary>
    private const int MinVisibleWidth = 120;

    public static PixelRect Restore(
        double? savedX,
        double? savedY,
        double savedWidth,
        double savedHeight,
        string? savedDisplayId,
        IReadOnlyList<DisplayAreaInfo> areas)
    {
        var primary = areas.FirstOrDefault(a => a.IsPrimary) ?? areas.FirstOrDefault();
        if (primary is null)
        {
            return new PixelRect(0, 0, (int)savedWidth, (int)savedHeight);
        }

        // The floating widget is deliberately fixed-size. Ignore historical
        // compact-window dimensions while preserving the saved position.
        int width = (int)FloatingWindowDefaults.FixedSize;
        int height = (int)FloatingWindowDefaults.FixedSize;

        // 尺寸超出当前工作区时限制到工作区。
        width = Math.Min(width, Math.Max(1, primary.WorkArea.Width));
        height = Math.Min(height, Math.Max(1, primary.WorkArea.Height));

        if (savedX is not { } xRaw || savedY is not { } yRaw)
        {
            return CenterOn(primary.WorkArea, width, height);
        }

        var saved = new PixelRect(
            (int)Math.Round(xRaw),
            (int)Math.Round(yRaw),
            width,
            height);

        // 优先使用最后记录的显示器；其次找与保存区域有交集的显示器；否则主显示器。
        var target = areas.FirstOrDefault(a => a.DisplayId == savedDisplayId)
            ?? areas.FirstOrDefault(a => IntersectsWithMargin(a.WorkArea, saved))
            ?? primary;

        if (IntersectsWithMargin(target.WorkArea, saved))
        {
            return ClampInto(target.WorkArea, saved);
        }

        // 保存坐标无效/显示器已移除：回到主显示器可见区域。
        return CenterOn(primary.WorkArea, width, height);
    }

    private static PixelRect ClampInto(PixelRect workArea, PixelRect rect)
    {
        int x = rect.X;
        int y = rect.Y;
        int width = Math.Min(rect.Width, workArea.Width);
        int height = Math.Min(rect.Height, workArea.Height);

        // 保证标题栏或可拖动区域可见（至少 MinVisibleHeight 在屏幕内）。
        if (y < workArea.Y - (height - MinVisibleHeight))
        {
            y = workArea.Y;
        }

        if (y + MinVisibleHeight > workArea.Y + workArea.Height)
        {
            y = workArea.Y + workArea.Height - MinVisibleHeight;
        }

        if (x + MinVisibleWidth > workArea.X + workArea.Width)
        {
            x = workArea.X + workArea.Width - MinVisibleWidth;
        }

        if (x < workArea.X - (width - MinVisibleWidth))
        {
            x = workArea.X;
        }

        return new PixelRect(Math.Max(x, workArea.X), Math.Max(y, workArea.Y), width, height);
    }

    private static bool IntersectsWithMargin(PixelRect workArea, PixelRect rect)
    {
        int visibleWidth = Math.Min(rect.Width, MinVisibleWidth);
        int visibleHeight = Math.Min(rect.Height, MinVisibleHeight);
        return rect.X < workArea.X + workArea.Width
            && rect.X + visibleWidth > workArea.X
            && rect.Y < workArea.Y + workArea.Height
            && rect.Y + visibleHeight > workArea.Y;
    }

    private static PixelRect CenterOn(PixelRect workArea, int width, int height)
    {
        int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
        return new PixelRect(x, y, width, height);
    }

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
}
