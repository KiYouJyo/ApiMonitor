namespace ApiMonitor.Models;

/// <summary>
/// 紧凑窗口的持久化设置。窗口尺寸/位置保存在物理像素（与 AppWindow 一致），
/// 恢复时由 WindowPositionRestorer 根据当前显示器工作区做安全钳制。
/// </summary>
public sealed class CompactWindowSettings
{
    public int SchemaVersion { get; set; } = Services.CompactWindowSettingsStore.CurrentSchemaVersion;

    /// <summary>始终置顶开关，默认开启。</summary>
    public bool IsAlwaysOnTop { get; set; } = true;

    /// <summary>最后选择的账户 ID（持久化主键，不使用显示名称）。</summary>
    public string? SelectedAccountId { get; set; }

    /// <summary>最后选择的币种。</summary>
    public string? SelectedCurrency { get; set; }

    public double Width { get; set; } = 360;

    public double Height { get; set; } = 240;

    /// <summary>保存的窗口左上角 X（物理像素，可为空表示未保存过）。</summary>
    public double? X { get; set; }

    /// <summary>保存的窗口左上角 Y（物理像素，可为空表示未保存过）。</summary>
    public double? Y { get; set; }

    /// <summary>最后显示窗口的显示器标识（DisplayId.Value 的字符串形式）。</summary>
    public string? LastDisplayId { get; set; }
}
