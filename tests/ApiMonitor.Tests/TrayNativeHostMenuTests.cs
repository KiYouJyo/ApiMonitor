using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 托盘菜单弹出流程测试（通过注入原生调用实现，CI 不真正弹出菜单）：
/// SetForegroundWindow 先于 TrackPopupMenuEx、菜单关闭后 WM_NULL + NIM_SETFOCUS、
/// 单菜单互斥、锚点坐标正确传递、全部定位失败时放弃显示。
/// </summary>
public sealed class TrayNativeHostMenuTests
{
    private static readonly Guid IconId = new("8D3E7F1A-2B4C-4D5E-9F0A-1B2C3D4E5F60");

    private static TrayMenuItem[] MenuItems() =>
    [
        new("打开 ApiMonitor", 0x1000, IsDefault: true),
        new("退出 ApiMonitor", 0x1004),
    ];

    private static TrayNativeHost CreateHost(
        List<string>? calls = null,
        uint? trackResult = null,
        TrayScreenPoint? cursorPoint = null)
    {
        var host = new TrayNativeHost("C:\\tray.ico", IconId);
        calls ??= new List<string>();
        host.SetForegroundWindowImpl = _ => calls.Add("SetForeground");
        host.TrackPopupMenuImpl = (h, f, x, y, w, t) =>
        {
            calls.Add($"TrackPopup:{x},{y}:0x{f:X}");
            return trackResult ?? 0;
        };
        host.PostNullMessageImpl = _ => calls.Add("WM_NULL");
        host.SetTrayFocusImpl = () => calls.Add("NIM_SETFOCUS");
        host.WorkAreaForPointImpl = p => new TrayScreenRect(0, 0, 1920, 1040);
        // 注入窗口句柄使窗口有效性检查通过，不创建真实窗口（CI 无桌面也可运行）。
        host.MessageWindowOverride = new IntPtr(0x1234);
        return host;
    }

    [Fact]
    public void SetForegroundWindow_RunsBeforeTrackPopupMenu()
    {
        var calls = new List<string>();
        var host = CreateHost(calls, trackResult: 0x1000);

        host.ShowContextMenu(MenuItems(), new TrayContextMenuRequest(new TrayScreenPoint(500, 600), TrayAnchorSource.Cursor));

        int setFg = calls.IndexOf("SetForeground");
        int track = calls.FindIndex(c => c.StartsWith("TrackPopup:", StringComparison.Ordinal));
        Assert.True(setFg >= 0 && track > setFg, $"顺序错误: {string.Join(" -> ", calls)}");
    }

    [Fact]
    public void AfterMenuClosed_PostNullAndSetFocus()
    {
        var calls = new List<string>();
        var host = CreateHost(calls, trackResult: 0);

        host.ShowContextMenu(MenuItems(), new TrayContextMenuRequest(new TrayScreenPoint(500, 600), TrayAnchorSource.Cursor));

        int track = calls.FindIndex(c => c.StartsWith("TrackPopup:", StringComparison.Ordinal));
        int wmNull = calls.IndexOf("WM_NULL");
        int setFocus = calls.IndexOf("NIM_SETFOCUS");
        Assert.True(track >= 0 && wmNull > track && setFocus > wmNull, $"顺序错误: {string.Join(" -> ", calls)}");
    }

    [Fact]
    public void CursorAnchor_IsPassedToTrackPopupMenuAsScreenCoordinates()
    {
        var calls = new List<string>();
        var host = CreateHost(calls, trackResult: 0);

        host.ShowContextMenu(MenuItems(), new TrayContextMenuRequest(new TrayScreenPoint(1234, 987), TrayAnchorSource.Cursor));

        Assert.Contains(calls, c => c.StartsWith("TrackPopup:1234,987:", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectedCommand_IsReturned()
    {
        var host = CreateHost(trackResult: 0x1004);

        uint? command = host.ShowContextMenu(MenuItems(), new TrayContextMenuRequest(new TrayScreenPoint(100, 100), TrayAnchorSource.Cursor));

        Assert.Equal(0x1004u, command);
    }

    [Fact]
    public void NoSelection_ReturnsNull()
    {
        var host = CreateHost(trackResult: 0);

        uint? command = host.ShowContextMenu(MenuItems(), new TrayContextMenuRequest(new TrayScreenPoint(100, 100), TrayAnchorSource.Cursor));

        Assert.Null(command);
    }

    [Fact]
    public void RapidReentrantRightClick_DoesNotOpenSecondMenu()
    {
        var host = CreateHost();
        int trackCalls = 0;
        bool secondReturnedNull = false;
        host.TrackPopupMenuImpl = (h, f, x, y, w, t) =>
        {
            trackCalls++;
            if (trackCalls == 1)
            {
                // 菜单尚未关闭时再次请求（重入）→ 应被互斥拒绝。
                var nested = host.ShowContextMenu(
                    MenuItems(),
                    new TrayContextMenuRequest(new TrayScreenPoint(10, 10), TrayAnchorSource.Cursor));
                secondReturnedNull = nested is null;
            }

            return 0;
        };

        host.ShowContextMenu(MenuItems(), new TrayContextMenuRequest(new TrayScreenPoint(100, 100), TrayAnchorSource.Cursor));

        Assert.True(secondReturnedNull);
        Assert.Equal(1, trackCalls);
    }

    [Fact]
    public void AllAnchorsInvalid_AbortsWithoutShowingMenu()
    {
        var host = CreateHost();
        host.WorkAreaForPointImpl = _ => null; // 所有候选坐标都不在任何显示器
        host.IconRectImpl = () => null;        // 图标矩形也失败
        int trackCalls = 0;
        host.TrackPopupMenuImpl = (h, f, x, y, w, t) =>
        {
            trackCalls++;
            return 0;
        };

        uint? command = host.ShowContextMenu(
            MenuItems(),
            new TrayContextMenuRequest(default, TrayAnchorSource.None));

        Assert.Null(command);
        Assert.Equal(0, trackCalls);
    }

    [Fact]
    public void IconRectAnchor_NearBottomTaskbar_ExpandsUpward()
    {
        var calls = new List<string>();
        var host = CreateHost(calls);
        host.IconRectImpl = () => new TrayScreenRect(1840, 980, 1900, 1020);

        host.ShowContextMenu(MenuItems(), new TrayContextMenuRequest(default, TrayAnchorSource.None));

        // 底部任务栏：TPM_BOTTOMALIGN(0x2000) + TPM_RIGHTALIGN(0x8)，锚点 = 图标左上角 (1840, 980)。
        // flags = 0x2 | 0x100 | 0x80 | 0x2008 = 0x218A
        Assert.Contains(calls, c => c == "TrackPopup:1840,980:0x218A");
    }

    [Fact]
    public void IconRectAnchor_NearTopTaskbar_ExpandsDownward()
    {
        var calls = new List<string>();
        var host = CreateHost(calls);
        host.IconRectImpl = () => new TrayScreenRect(1840, 10, 1900, 50);

        host.ShowContextMenu(MenuItems(), new TrayContextMenuRequest(default, TrayAnchorSource.None));

        // 顶部任务栏：TPM_TOPALIGN(0x0) + TPM_RIGHTALIGN(0x8)，锚点 = (1840, 50)。
        // flags = 0x2 | 0x100 | 0x80 | 0x8 = 0x18A
        Assert.Contains(calls, c => c == "TrackPopup:1840,50:0x18A");
    }
}
