using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// NOTIFYICON_VERSION_4 回调解析测试（需求：事件类型取 LOWORD(lParam)、
/// 图标 ID 取 HIWORD(lParam)、WM_CONTEXTMENU 坐标锚点在 wParam）。
/// </summary>
public sealed class TrayCallbackParserTests
{
    [Fact]
    public void ContextMenu_ReadFromLowWordOfLParam()
    {
        // lParam = 0x0000007B（WM_CONTEXTMENU，图标 ID=0）
        var evt = TrayCallbackParser.ParseTrayCallback(new nint(0x012F00C8), new nint(0x0000007B));

        Assert.Equal(TrayCallbackEventKind.ContextMenu, evt.Kind);
        Assert.True(evt.IsContextMenu);
    }

    [Fact]
    public void ContextMenu_WithIconIdInHighWord()
    {
        // lParam 高 16 位 = 图标 ID 0x0102
        var evt = TrayCallbackParser.ParseTrayCallback(new nint(0), new nint(0x0102007B));

        Assert.Equal(TrayCallbackEventKind.ContextMenu, evt.Kind);
        Assert.Equal((ushort)0x0102, evt.IconId);
    }

    [Fact]
    public void LeftClick_ReadFromLowWord()
    {
        var evt = TrayCallbackParser.ParseTrayCallback(new nint(0), new nint(0x00000202));

        Assert.Equal(TrayCallbackEventKind.LeftClick, evt.Kind);
    }

    [Fact]
    public void LeftDoubleClick_ReadFromLowWord()
    {
        var evt = TrayCallbackParser.ParseTrayCallback(new nint(0), new nint(0x00000203));

        Assert.Equal(TrayCallbackEventKind.LeftDoubleClick, evt.Kind);
    }

    [Fact]
    public void UnknownEvent_MapsToOther()
    {
        var evt = TrayCallbackParser.ParseTrayCallback(new nint(0), new nint(0x00000205)); // WM_RBUTTONUP

        Assert.Equal(TrayCallbackEventKind.Other, evt.Kind);
        Assert.Null(evt.WParamPoint);
    }

    [Fact]
    public void ContextMenu_CoordinatesFromWParam()
    {
        // wParam = (200, 303) 打包进高低 16 位（GET_X/Y_LPARAM 语义）
        var evt = TrayCallbackParser.ParseTrayCallback(new nint(0x012F00C8), new nint(0x0000007B));

        Assert.NotNull(evt.WParamPoint);
        Assert.Equal(200, evt.WParamPoint!.Value.X);
        Assert.Equal(303, evt.WParamPoint!.Value.Y);
    }

    [Fact]
    public void ContextMenu_NegativeCoordinatesFromWParam()
    {
        // x = -100 (0xFF9C), y = -50 (0xFFCE)
        var evt = TrayCallbackParser.ParseTrayCallback(new nint(0xFFCEFF9C), new nint(0x0000007B));

        Assert.Equal(-100, evt.WParamPoint!.Value.X);
        Assert.Equal(-50, evt.WParamPoint!.Value.Y);
    }

    [Fact]
    public void NonContextMenuEvent_HasNoWParamPoint()
    {
        var evt = TrayCallbackParser.ParseTrayCallback(new nint(0x012F00C8), new nint(0x00000202));

        Assert.Null(evt.WParamPoint);
    }
}
