namespace ApiMonitor.Models;

/// <summary>
/// 悬浮余额窗的持久化设置（v0.7.0 起，文件 floating-window-settings.json）。
/// 窗口尺寸/位置保存在物理像素（与 AppWindow 一致），恢复时由
/// WindowPositionRestorer 根据当前显示器工作区做安全钳制。
/// 旧 compact-window-settings.json 只用于一次性迁移读取，不再对外沿用
/// “紧凑窗口”命名。
/// </summary>
public sealed class FloatingWindowSettings
{
    public int SchemaVersion { get; set; } = Services.FloatingWindowSettingsStore.CurrentSchemaVersion;

    /// <summary>始终置顶开关，默认开启（v0.7.0 固定为开启，不做 UI 配置）。</summary>
    public bool IsAlwaysOnTop { get; set; } = true;

    /// <summary>最后选择的账户 ID（持久化主键，不使用显示名称）。</summary>
    public string? SelectedAccountId { get; set; }

    public double Width { get; set; } = Services.FloatingWindowDefaults.DefaultWidth;

    public double Height { get; set; } = Services.FloatingWindowDefaults.DefaultHeight;

    /// <summary>保存的窗口左上角 X（物理像素，可为空表示未保存过）。</summary>
    public double? X { get; set; }

    /// <summary>保存的窗口左上角 Y（物理像素，可为空表示未保存过）。</summary>
    public double? Y { get; set; }

    /// <summary>最后显示窗口的显示器标识（DisplayId.Value 的字符串形式）。</summary>
    public string? LastDisplayId { get; set; }
}
