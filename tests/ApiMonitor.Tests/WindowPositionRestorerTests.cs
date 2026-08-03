using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>窗口位置/尺寸恢复逻辑测试（不涉及真实显示器）。</summary>
public sealed class WindowPositionRestorerTests
{
    private static readonly IReadOnlyList<DisplayAreaInfo> SingleDisplay =
        new[]
        {
            new DisplayAreaInfo("primary", new PixelRect(0, 0, 1920, 1040), true),
        };

    private static readonly IReadOnlyList<DisplayAreaInfo> DualDisplay =
        new[]
        {
            new DisplayAreaInfo("left", new PixelRect(0, 0, 1920, 1040), true),
            new DisplayAreaInfo("right", new PixelRect(1920, 0, 1920, 1040), false),
        };

    [Fact]
    public void Restore_ValidPosition_IsKeptOnSameDisplay()
    {
        var restored = WindowPositionRestorer.Restore(
            120, 80, 360, 240, "primary", DualDisplay);

        Assert.Equal(120, restored.X);
        Assert.Equal(80, restored.Y);
        Assert.Equal(360, restored.Width);
        Assert.Equal(240, restored.Height);
    }

    [Fact]
    public void Restore_OffScreenPosition_CentersOnPrimary()
    {
        var restored = WindowPositionRestorer.Restore(
            5000, 5000, 360, 240, "primary", DualDisplay);

        Assert.True(restored.X >= 0 && restored.X < 1920);
        Assert.True(restored.Y >= 0 && restored.Y < 1040);
        Assert.Equal(360, restored.Width);
        Assert.Equal(240, restored.Height);
    }

    [Fact]
    public void Restore_NoSavedPosition_CentersOnPrimary()
    {
        var restored = WindowPositionRestorer.Restore(
            null, null, 360, 240, null, DualDisplay);

        Assert.Equal((1920 - 360) / 2, restored.X);
        Assert.Equal((1040 - 240) / 2, restored.Y);
    }

    [Fact]
    public void Restore_OversizedWindow_IsClampedToWorkArea()
    {
        var restored = WindowPositionRestorer.Restore(
            0, 0, 4000, 4000, "primary", SingleDisplay);

        Assert.True(restored.Width <= 1920);
        Assert.True(restored.Height <= 1040);
    }

    [Fact]
    public void Restore_RemovedSecondaryDisplay_FallsBackToPrimary()
    {
        // 保存区域位于已移除的副屏上。
        var restored = WindowPositionRestorer.Restore(
            2500, 300, 360, 240, "right", SingleDisplay);

        Assert.True(restored.X + restored.Width <= 1920);
        Assert.True(restored.Y >= 0);
    }

    [Fact]
    public void Restore_TooSmallSize_IsRaisedToMinimum()
    {
        var restored = WindowPositionRestorer.Restore(
            0, 0, 10, 10, "primary", SingleDisplay);

        Assert.True(restored.Width >= CompactWindowDefaults.MinWidth);
        Assert.True(restored.Height >= CompactWindowDefaults.MinHeight);
    }

    [Fact]
    public void Restore_DisplayIdMatch_IsPreferred()
    {
        var restored = WindowPositionRestorer.Restore(
            2000, 100, 360, 240, "right", DualDisplay);

        Assert.True(restored.X >= 1920);
    }
}
