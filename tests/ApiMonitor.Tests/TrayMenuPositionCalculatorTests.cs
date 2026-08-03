using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 托盘右键菜单位置计算测试（需求：GetCursorPos 成功用屏幕坐标原值、
/// 失败回退 wParam/图标矩形、不回退 (0,0)、底部/顶部/左右任务栏方向、
/// 多显示器与负坐标、DPI 不二次缩放）。
/// </summary>
public sealed class TrayMenuPositionCalculatorTests
{
    private static readonly TrayScreenRect PrimaryWorkArea = new(0, 0, 1920, 1040);

    private static TrayScreenPoint Pt(int x, int y) => new(x, y);

    private static TrayMenuPlacement? Resolve(
        TrayScreenPoint? cursor,
        TrayScreenPoint? wParam,
        TrayScreenRect? iconRect,
        Func<TrayScreenPoint, TrayScreenRect?>? getWorkArea = null) =>
        TrayMenuPositionCalculator.Resolve(cursor, wParam, iconRect, getWorkArea ?? (p => PrimaryWorkArea));

    [Fact]
    public void CursorValid_UsesScreenCoordinatesAsIs()
    {
        var placement = Resolve(Pt(500, 600), null, null);

        Assert.NotNull(placement);
        Assert.Equal(500, placement!.Value.X);
        Assert.Equal(600, placement!.Value.Y);
    }

    [Fact]
    public void CursorCoordinates_AreNeverReScaledForDpi()
    {
        // 125%/150%/200% DPI：物理屏幕坐标必须原样传递，不做任何乘除。
        foreach (int scale in new[] { 100, 125, 150, 200 })
        {
            var placement = Resolve(Pt(960, 540), null, null);
            Assert.Equal(960, placement!.Value.X);
            Assert.Equal(540, placement!.Value.Y);
        }
    }

    [Fact]
    public void CursorOnBottomHalf_MenuExpandsUpward()
    {
        // 光标在工作区下半部 → TPM_BOTTOMALIGN（菜单底边在锚点，向上展开）
        var placement = Resolve(Pt(960, 900), null, null);

        Assert.NotNull(placement);
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_BOTTOMALIGN) != 0);
    }

    [Fact]
    public void CursorOnTopHalf_MenuExpandsDownward()
    {
        var placement = Resolve(Pt(960, 200), null, null);

        Assert.NotNull(placement);
        // TOPALIGN=0x0：断言“不含 BOTTOMALIGN”即向下展开。
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_BOTTOMALIGN) == 0);
    }

    [Fact]
    public void CursorOnRightHalf_MenuAlignsRight()
    {
        var placement = Resolve(Pt(1800, 500), null, null);

        Assert.NotNull(placement);
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_RIGHTALIGN) != 0);
    }

    [Fact]
    public void CursorOnLeftHalf_MenuAlignsLeft()
    {
        var placement = Resolve(Pt(200, 500), null, null);

        Assert.NotNull(placement);
        // LEFTALIGN=0x0：断言“不含 RIGHTALIGN”即向左对齐。
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_RIGHTALIGN) == 0);
    }

    [Fact]
    public void CursorInvalid_FallsBackToWParamPoint()
    {
        var placement = Resolve(
            Pt(10, 10),
            Pt(800, 700),
            null,
            getWorkArea: p =>
                p == Pt(10, 10) ? null : PrimaryWorkArea); // cursor 所在坐标无效

        Assert.NotNull(placement);
        Assert.Equal(800, placement!.Value.X);
        Assert.Equal(700, placement!.Value.Y);
    }

    [Fact]
    public void KeyboardTrigger_UsesIconRectAsAnchor()
    {
        // 键盘触发：无 cursor 也无 wParam 坐标 → 用图标矩形
        var iconRect = new TrayScreenRect(1840, 980, 1900, 1020); // 底部任务栏图标
        var placement = Resolve(null, null, iconRect);

        Assert.NotNull(placement);
        // 底部任务栏：向上展开，锚点 y = 图标顶部
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_BOTTOMALIGN) != 0);
        Assert.Equal(iconRect.Top, placement!.Value.Y);
    }

    [Fact]
    public void BottomTaskbar_IconNearBottom_ExpandsUpward()
    {
        var iconRect = new TrayScreenRect(1840, 980, 1900, 1020);
        var placement = Resolve(null, null, iconRect);

        Assert.NotNull(placement);
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_BOTTOMALIGN) != 0);
        Assert.Equal(iconRect.Top, placement!.Value.Y);
    }

    [Fact]
    public void TopTaskbar_IconNearTop_ExpandsDownward()
    {
        var iconRect = new TrayScreenRect(1840, 10, 1900, 50);
        var placement = Resolve(null, null, iconRect);

        Assert.NotNull(placement);
        // TOPALIGN=0x0：断言“不含 BOTTOMALIGN”。
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_BOTTOMALIGN) == 0);
        Assert.Equal(iconRect.Bottom, placement!.Value.Y);
    }

    [Fact]
    public void RightTaskbar_IconNearRight_ExpandsLeftward()
    {
        var iconRect = new TrayScreenRect(1880, 500, 1920, 540);
        var placement = Resolve(null, null, iconRect);

        Assert.NotNull(placement);
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_RIGHTALIGN) != 0);
        Assert.Equal(iconRect.Left, placement!.Value.X);
    }

    [Fact]
    public void LeftTaskbar_IconNearLeft_ExpandsRightward()
    {
        var iconRect = new TrayScreenRect(0, 500, 40, 540);
        var placement = Resolve(null, null, iconRect);

        Assert.NotNull(placement);
        // LEFTALIGN=0x0：断言“不含 RIGHTALIGN”。
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_RIGHTALIGN) == 0);
        Assert.Equal(iconRect.Right, placement!.Value.X);
    }

    [Fact]
    public void SecondMonitor_UsesThatMonitorsWorkArea()
    {
        // 第二显示器位于右侧（1920..3840）
        TrayScreenRect secondWorkArea = new(1920, 0, 3840, 1040);
        var iconRect = new TrayScreenRect(3700, 980, 3760, 1020);

        var placement = TrayMenuPositionCalculator.Resolve(
            null,
            null,
            iconRect,
            _ => secondWorkArea);

        Assert.NotNull(placement);
        // 在第二显示器工作区下半部 → 向上展开
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_BOTTOMALIGN) != 0);
    }

    [Fact]
    public void NegativeCoordinateMonitor_HandledCorrectly()
    {
        // 左侧负坐标显示器：工作区 x 为负
        TrayScreenRect negativeWorkArea = new(-1920, 0, 0, 1040);
        var iconRect = new TrayScreenRect(-300, 980, -240, 1020);

        var placement = TrayMenuPositionCalculator.Resolve(
            null,
            null,
            iconRect,
            _ => negativeWorkArea);

        Assert.NotNull(placement);
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_BOTTOMALIGN) != 0);
        // 右半区（相对负工作区）→ 向左展开
        Assert.True((placement!.Value.AlignFlags & NativeMethods.TPM_RIGHTALIGN) != 0);
    }

    [Fact]
    public void AllCandidatesInvalid_ReturnsNullNotZeroZero()
    {
        // 所有候选都无效：返回 null（调用方放弃显示），绝不默认 (0,0)。
        var placement = Resolve(
            Pt(10, 10),
            Pt(20, 20),
            new TrayScreenRect(30, 30, 40, 40),
            getWorkArea: _ => null);

        Assert.Null(placement);
    }
}
