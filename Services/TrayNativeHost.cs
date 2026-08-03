using System.Runtime.InteropServices;

namespace ApiMonitor.Services;

/// <summary>
/// 通知区域 Win32 原生宿主实现：
/// - 独立的轻量隐藏消息窗口（不绑定任何可见 WinUI 窗口的 HWND）；
///   窗口保持 enabled（WS_POPUP + WS_EX_TOOLWINDOW，从未 ShowWindow，
///   不会出现在任务栏/Alt+Tab，也不会抢焦点），使托盘右键菜单可获得键盘焦点；
/// - Shell_NotifyIconW（Unicode）+ NOTIFYICON_VERSION_4 + 稳定 GUID；
/// - RegisterWindowMessage("TaskbarCreated") 监听 Explorer 重启；
/// - 原生弹出菜单（CreatePopupMenu + TrackPopupMenuEx，TPM_RETURNCMD）。
/// 所有方法必须从持有消息窗口的线程（UI 线程）调用。
/// 注意：不要给消息窗口加 WS_DISABLED——那会导致 SetForegroundWindow 失败、
/// 弹出菜单失去键盘焦点。
/// </summary>
internal sealed class TrayNativeHost : ITrayNativeHost
{
    private const string WindowClassName = "ApiMonitorTrayMessageWindow";

    private readonly Guid _iconId;
    private readonly string _iconFilePath;
    private readonly AppLog? _log;
    private readonly object _gate = new();

    private NativeMethods.WindowProc? _windowProc; // 必须在整个生命周期保持引用，防止被 GC 回收。
    private IntPtr _hWnd = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _classRegistered;
    private uint _taskbarCreatedMessage;
    private bool _isMenuOpen;

    private string _tooltipText = string.Empty;

    // 测试注入点：覆盖原生调用以验证顺序，CI 不真正弹出托盘菜单。
    internal Func<IntPtr, uint, int, int, IntPtr, IntPtr, nuint>? TrackPopupMenuImpl { get; set; }
    internal Action<IntPtr>? SetForegroundWindowImpl { get; set; }
    internal Action<IntPtr>? PostNullMessageImpl { get; set; }
    internal Action? SetTrayFocusImpl { get; set; }
    internal Func<TrayScreenRect?>? IconRectImpl { get; set; }
    internal Func<TrayScreenPoint, TrayScreenRect?>? WorkAreaForPointImpl { get; set; }
    internal IntPtr? MessageWindowOverride { get; set; }

    public TrayNativeHost(string iconFilePath, Guid iconId, AppLog? log = null)
    {
        _iconFilePath = iconFilePath;
        _iconId = iconId;
        _log = log;
    }

    public event Action? LeftClick;
    public event Action? LeftDoubleClick;
    public event Action<TrayContextMenuRequest>? ContextMenuRequested;
    public event Action? TaskbarCreated;

    public bool IsMessageWindowAlive => _hWnd != IntPtr.Zero;

    public bool Initialize()
    {
        lock (_gate)
        {
            if (_hWnd != IntPtr.Zero)
            {
                return true;
            }

            try
            {
                _taskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
                if (_taskbarCreatedMessage == 0)
                {
                    _taskbarCreatedMessage = NativeMethods.WM_USER + 0x7FF;
                }

                RegisterWindowClass();

                var hInstance = NativeMethods.GetModuleHandleW(null);
                _hWnd = NativeMethods.CreateWindowExW(
                    NativeMethods.WS_EX_TOOLWINDOW,
                    WindowClassName,
                    "ApiMonitorTrayWindow",
                    NativeMethods.WS_POPUP,
                    0,
                    0,
                    0,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    hInstance,
                    IntPtr.Zero);

                if (_hWnd == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    _log?.Error($"托盘消息窗口创建失败 (Win32 error {error})。");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _log?.Error($"托盘消息窗口初始化异常: {ex.GetType().Name}");
                return false;
            }
        }
    }

    public bool AddIcon(string tooltipText)
    {
        lock (_gate)
        {
            if (_hWnd == IntPtr.Zero && !Initialize())
            {
                return false;
            }

            try
            {
                if (_hIcon == IntPtr.Zero)
                {
                    _hIcon = NativeMethods.LoadImageW(
                        IntPtr.Zero,
                        _iconFilePath,
                        NativeMethods.IMAGE_ICON,
                        0,
                        0,
                        NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE);
                }

                _tooltipText = SafeTooltip(tooltipText);

                var data = NativeMethods.NOTIFYICONDATAW.Create();
                data.hWnd = _hWnd;
                data.uID = 0;
                data.uFlags = NativeMethods.NIF_MESSAGE
                    | NativeMethods.NIF_ICON
                    | NativeMethods.NIF_TIP
                    | NativeMethods.NIF_GUID
                    | NativeMethods.NIF_SHOWTIP;
                data.uCallbackMessage = NativeMethods.TRAY_CALLBACK_MESSAGE;
                data.hIcon = _hIcon;
                data.szTip = _tooltipText;
                data.guidItem = _iconId;

                bool added = NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref data);
                if (!added)
                {
                    return false;
                }

                // NOTIFYICON_VERSION_4：新的上下文菜单消息行为。
                var versionData = NativeMethods.NOTIFYICONDATAW.Create();
                versionData.hWnd = _hWnd;
                versionData.uID = 0;
                versionData.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_GUID;
                versionData.uCallbackMessage = NativeMethods.TRAY_CALLBACK_MESSAGE;
                versionData.uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
                versionData.guidItem = _iconId;
                NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_SETVERSION, ref versionData);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool UpdateTip(string tooltipText)
    {
        lock (_gate)
        {
            if (_hWnd == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                _tooltipText = SafeTooltip(tooltipText);
                var data = NativeMethods.NOTIFYICONDATAW.Create();
                data.hWnd = _hWnd;
                data.uID = 0;
                data.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_GUID;
                data.szTip = _tooltipText;
                data.guidItem = _iconId;
                return NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref data);
            }
            catch
            {
                return false;
            }
        }
    }

    public bool DeleteIcon()
    {
        lock (_gate)
        {
            if (_hWnd == IntPtr.Zero)
            {
                return true;
            }

            try
            {
                var data = NativeMethods.NOTIFYICONDATAW.Create();
                data.hWnd = _hWnd;
                data.uID = 0;
                data.uFlags = NativeMethods.NIF_GUID;
                data.guidItem = _iconId;
                return NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref data);
            }
            catch
            {
                return false;
            }
        }
    }

    public uint? ShowContextMenu(IReadOnlyList<TrayMenuItem> items, TrayContextMenuRequest request)
    {
        if ((MessageWindowOverride is null && _hWnd == IntPtr.Zero) || _isMenuOpen)
        {
            // 同一时刻只允许一个托盘菜单；连续快速右键直接忽略。
            return null;
        }

        _isMenuOpen = true;
        IntPtr menu = IntPtr.Zero;
        try
        {
            // 解析最终锚点与展开方向（屏幕坐标，物理像素，不做任何缩放/客户区转换）。
            var placement = ResolvePlacement(request);
            if (placement is null)
            {
                _log?.Error("托盘菜单位置解析失败，放弃显示（不会弹出到屏幕左上角）。");
                return null;
            }

            _log?.Info($"托盘菜单锚点来源: {request.Source}，位置 ({placement.Value.X}, {placement.Value.Y})。");

            menu = NativeMethods.CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return null;
            }

            foreach (var item in items)
            {
                uint flags = NativeMethods.MF_STRING;
                if (item.IsSeparator)
                {
                    flags = NativeMethods.MF_SEPARATOR;
                }
                else
                {
                    if (item.IsDefault)
                    {
                        flags |= NativeMethods.MF_DEFAULT;
                    }

                    if (!item.IsEnabled)
                    {
                        flags |= NativeMethods.MF_GRAYED | NativeMethods.MF_DISABLED;
                    }

                    if (item.IsChecked)
                    {
                        flags |= NativeMethods.MF_CHECKED;
                    }
                }

                if (!NativeMethods.AppendMenuW(menu, flags, item.CommandId, item.Text))
                {
                    return null;
                }
            }

            // 菜单显示前：把隐藏消息窗口设为前台窗口，保证菜单获得键盘焦点。
            if (SetForegroundWindowImpl is { } setForeground)
            {
                setForeground(_hWnd);
            }
            else
            {
                NativeMethods.SetForegroundWindow(_hWnd);
            }

            uint trackFlags = NativeMethods.TPM_RIGHTBUTTON
                | NativeMethods.TPM_RETURNCMD
                | NativeMethods.TPM_NONOTIFY
                | placement.Value.AlignFlags;

            nuint result = TrackPopupMenuImpl is { } track
                ? track(menu, trackFlags, placement.Value.X, placement.Value.Y, _hWnd, IntPtr.Zero)
                : NativeMethods.TrackPopupMenuEx(menu, trackFlags, placement.Value.X, placement.Value.Y, _hWnd, IntPtr.Zero);

            // 菜单已关闭：把焦点安全返回通知区域，再分发命令。
            if (PostNullMessageImpl is { } postNull)
            {
                postNull(_hWnd);
            }
            else
            {
                NativeMethods.PostMessageW(_hWnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            }

            if (SetTrayFocusImpl is { } setFocus)
            {
                setFocus();
            }
            else
            {
                SetTrayFocus();
            }

            return result == 0 ? null : (uint)result;
        }
        finally
        {
            _isMenuOpen = false;
            if (menu != IntPtr.Zero)
            {
                NativeMethods.DestroyMenu(menu);
            }
        }
    }

    /// <summary>把焦点返回通知区域（NIM_SETFOCUS），不激活主窗口。</summary>
    private void SetTrayFocus()
    {
        try
        {
            var data = NativeMethods.NOTIFYICONDATAW.Create();
            data.hWnd = _hWnd;
            data.uID = 0;
            data.uFlags = NativeMethods.NIF_GUID;
            data.guidItem = _iconId;
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_SETFOCUS, ref data);
        }
        catch
        {
            // 焦点返回失败不影响菜单命令分发。
        }
    }

    /// <summary>
    /// 按锚点优先级解析最终菜单位置：
    /// GetCursorPos 屏幕坐标 → wParam 坐标 → Shell_NotifyIconGetRect 图标矩形。
    /// 全部失败返回 null（放弃显示），绝不使用未经验证的 (0,0)。
    /// </summary>
    internal TrayMenuPlacement? ResolvePlacement(TrayContextMenuRequest request)
    {
        TrayScreenPoint? cursor = request.Source == TrayAnchorSource.Cursor
            ? request.Point
            : null;
        TrayScreenPoint? wParamPoint = request.Source == TrayAnchorSource.WParam
            ? request.Point
            : null;
        TrayScreenRect? iconRect = TryGetIconRect();

        return TrayMenuPositionCalculator.Resolve(
            cursor,
            wParamPoint,
            iconRect,
            GetWorkAreaForPoint);
    }

    private TrayScreenRect? GetWorkAreaForPoint(TrayScreenPoint point)
    {
        if (WorkAreaForPointImpl is { } injected)
        {
            return injected(point);
        }

        try
        {
            var pt = new NativeMethods.POINT { X = point.X, Y = point.Y };
            IntPtr monitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONULL);
            if (monitor == IntPtr.Zero)
            {
                return null;
            }

            var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfoW(monitor, ref info))
            {
                return null;
            }

            return new TrayScreenRect(
                info.rcWork.Left,
                info.rcWork.Top,
                info.rcWork.Right,
                info.rcWork.Bottom);
        }
        catch
        {
            return null;
        }
    }

    private TrayScreenRect? TryGetIconRect()
    {
        if (IconRectImpl is { } injected)
        {
            return injected();
        }

        try
        {
            var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
                hWnd = _hWnd,
                uID = 0,
                guidItem = _iconId,
            };

            if (!NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var rect))
            {
                return null;
            }

            return new TrayScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DeleteIcon();

            if (_hIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }

            if (_hWnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_hWnd);
                _hWnd = IntPtr.Zero;
            }

            _windowProc = null;
        }
    }

    private void RegisterWindowClass()
    {
        if (_classRegistered)
        {
            return;
        }

        _windowProc = WindowProc;
        var wndClass = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = WindowClassName,
        };

        if (NativeMethods.RegisterClassExW(ref wndClass) != 0)
        {
            _classRegistered = true;
        }
        else
        {
            // 类已存在（异常路径）时仍可继续，CreateWindowExW 会复用。
            _classRegistered = true;
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMessage)
        {
            TaskbarCreated?.Invoke();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.TRAY_CALLBACK_MESSAGE)
        {
            var trayEvent = TrayCallbackParser.ParseTrayCallback(wParam, lParam);
            switch (trayEvent.Kind)
            {
                case TrayCallbackEventKind.LeftClick:
                    LeftClick?.Invoke();
                    return IntPtr.Zero;

                case TrayCallbackEventKind.LeftDoubleClick:
                    LeftDoubleClick?.Invoke();
                    return IntPtr.Zero;

                case TrayCallbackEventKind.ContextMenu:
                    // 同一线程立即获取鼠标屏幕坐标（NOTIFYICON_VERSION_4 的 wParam 坐标为后备）。
                    if (NativeMethods.GetCursorPos(out var pt))
                    {
                        ContextMenuRequested?.Invoke(
                            new TrayContextMenuRequest(new TrayScreenPoint(pt.X, pt.Y), TrayAnchorSource.Cursor));
                    }
                    else if (trayEvent.WParamPoint is { } wParamPoint)
                    {
                        ContextMenuRequested?.Invoke(
                            new TrayContextMenuRequest(wParamPoint, TrayAnchorSource.WParam));
                    }
                    else
                    {
                        ContextMenuRequested?.Invoke(
                            new TrayContextMenuRequest(default, TrayAnchorSource.None));
                    }

                    return IntPtr.Zero;

                default:
                    return IntPtr.Zero;
            }
        }

        if (msg == NativeMethods.WM_DESTROY)
        {
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>把 Tooltip 安全截断到 NOTIFYICONDATA 支持的长度（128 WCHAR，含结尾 NUL）。</summary>
    private static string SafeTooltip(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= NativeMethods.MAX_TIP_LENGTH - 1
            ? text
            : text[..(NativeMethods.MAX_TIP_LENGTH - 1)];
    }
}
