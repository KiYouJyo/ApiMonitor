namespace ApiMonitor.Services;

/// <summary>托盘原生层的屏幕坐标。</summary>
public readonly record struct TrayScreenPoint(int X, int Y);

/// <summary>原生菜单项描述（构建菜单用的纯数据，便于测试菜单状态）。</summary>
public sealed record TrayMenuItem(
    string? Text,
    uint CommandId,
    bool IsEnabled = true,
    bool IsChecked = false,
    bool IsDefault = false,
    bool IsSeparator = false);

/// <summary>
/// 通知区域原生宿主抽象：隐藏消息窗口 + Shell_NotifyIcon + 原生菜单。
/// 真实实现包装 Win32 API；测试用假实现记录调用并模拟回调。
/// </summary>
public interface ITrayNativeHost : IDisposable
{
    /// <summary>托盘图标左键单击（双击时不会同时触发单击）。</summary>
    event Action? LeftClick;

    /// <summary>托盘图标左键双击。</summary>
    event Action? LeftDoubleClick;

    /// <summary>托盘图标右键（要求弹出上下文菜单，参数为屏幕坐标）。</summary>
    event Action<TrayScreenPoint>? ContextMenuRequested;

    /// <summary>Explorer 重启后收到 TaskbarCreated 消息。</summary>
    event Action? TaskbarCreated;

    /// <summary>创建隐藏消息窗口。重复调用无副作用；失败时返回 false（不崩溃）。</summary>
    bool Initialize();

    /// <summary>添加图标并设置 NOTIFYICON_VERSION_4。重复添加前应先 DeleteIcon。</summary>
    bool AddIcon(string tooltipText);

    /// <summary>更新 Tooltip 文本（NIM_MODIFY）。</summary>
    bool UpdateTip(string tooltipText);

    /// <summary>删除图标（NIM_DELETE）。</summary>
    bool DeleteIcon();

    /// <summary>
    /// 在指定屏幕坐标弹出原生菜单并返回选中的命令 ID；未选择返回 null。
    /// 阻塞直到菜单关闭；必须从持有消息窗口的线程调用。
    /// </summary>
    uint? ShowContextMenu(IReadOnlyList<TrayMenuItem> items, TrayScreenPoint position);

    /// <summary>消息窗口句柄是否仍然有效（供生命周期状态机判断）。</summary>
    bool IsMessageWindowAlive { get; }
}
