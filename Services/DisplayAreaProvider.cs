using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ApiMonitor.Services;

/// <summary>
/// 基于 Microsoft.UI.Windowing.DisplayArea 的真实显示器工作区实现。
/// </summary>
public sealed class DisplayAreaProvider : IDisplayAreaProvider
{
    public IReadOnlyList<DisplayAreaInfo> GetAll() =>
        DisplayArea.FindAll()
            .Select(ToInfo)
            .ToList();

    public DisplayAreaInfo GetPrimary() => ToInfo(DisplayArea.Primary);

    public DisplayAreaInfo GetContaining(PixelRect rect)
    {
        var point = new PointInt32(
            rect.X + Math.Max(0, rect.Width / 2),
            rect.Y + Math.Max(0, rect.Height / 2));
        return ToInfo(DisplayArea.GetFromPoint(point, DisplayAreaFallback.Primary));
    }

    public DisplayAreaInfo GetByDisplayId(string displayId)
    {
        foreach (var area in DisplayArea.FindAll())
        {
            if (area.DisplayId.Value.ToString() == displayId)
            {
                return ToInfo(area);
            }
        }

        return GetPrimary();
    }

    private static DisplayAreaInfo ToInfo(DisplayArea area) =>
        new(
            area.DisplayId.Value.ToString(),
            new PixelRect(
                area.WorkArea.X,
                area.WorkArea.Y,
                area.WorkArea.Width,
                area.WorkArea.Height),
            area.IsPrimary);
}
