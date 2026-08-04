namespace ApiMonitor.Models;

/// <summary>
/// 主窗口点击右上角关闭 / Alt+F4 时的行为（v0.4.0 新增）。
/// </summary>
public enum MainWindowCloseBehavior
{
    /// <summary>取消真正关闭，隐藏到通知区域，进程继续运行。</summary>
    HideToTray,

    /// <summary>执行统一的显式退出流程（删除托盘图标、停止调度、进程退出）。</summary>
    ExitApplication,
}

/// <summary>
/// StartupTask 系统状态（映射自 Windows.ApplicationModel.StartupTaskState，
/// 用独立枚举避免 UI/测试层直接依赖 WinRT 类型）。
/// </summary>
public enum StartupTaskStatus
{
    Unknown,

    Disabled,

    DisabledByUser,

    DisabledByPolicy,

    Enabled,

    EnabledByPolicy,
}

/// <summary>
/// 托盘驻留与启动相关的持久化设置（tray-settings.json）。
/// 系统实际启用状态以 StartupTask 为准；这里只保存 UI 偏好与最后已知状态，
/// 不伪造系统启用状态。
/// </summary>
public sealed class TraySettings
{
    /// <summary>
    /// v0.5.0 设置文件版本（独立于账户/余额/悬浮窗/通知设置文件）。
    /// 从 4 升级到 5：v0.4.0 文件保留全部已知字段。
    /// </summary>
    public int SchemaVersion { get; set; } = 5;

    /// <summary>关闭主窗口时的行为，默认隐藏到通知区域。</summary>
    public MainWindowCloseBehavior MainWindowCloseBehavior { get; set; } = MainWindowCloseBehavior.HideToTray;

    /// <summary>首次隐藏到托盘前是否显示说明（用户可选择不再提示）。</summary>
    public bool ShowFirstCloseExplanation { get; set; } = true;

    /// <summary>用户界面偏好：是否希望登录 Windows 时启动。系统状态权威来源是 StartupTask。</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>登录启动时默认仅驻留通知区域，不弹出主窗口。</summary>
    public bool StartMinimizedToTray { get; set; } = true;

    /// <summary>最近一次从系统读取的 StartupTask 状态（仅用于 UI 快速展示，不覆盖系统状态）。</summary>
    public StartupTaskStatus? LastKnownStartupTaskState { get; set; }

    /// <summary>通知区域图标功能开关。本版本默认启用。</summary>
    public bool TrayFeatureEnabled { get; set; } = true;
}
