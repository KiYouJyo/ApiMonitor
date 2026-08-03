namespace ApiMonitor.Models;

/// <summary>
/// 全局通知设置（notification-settings.json）。
/// 升级后的默认行为：全局系统提醒默认关闭；已有阈值继续保留；
/// 用户主动开启后才发送 Windows 通知。
/// </summary>
public sealed class NotificationGlobalSettings
{
    /// <summary>是否启用余额系统提醒（默认关闭）。</summary>
    public bool BalanceNotificationsEnabled { get; set; }

    /// <summary>默认重复提醒间隔（小时）；0 = 不重复。默认 24 小时。</summary>
    public int DefaultRepeatIntervalHours { get; set; } = 24;

    /// <summary>是否启用余额恢复提醒（默认开启）。</summary>
    public bool RecoveryNotificationsEnabled { get; set; } = true;
}

/// <summary>允许的重复提醒间隔选项（小时）。</summary>
public static class NotificationRepeatIntervals
{
    public const int None = 0;
    public const int SixHours = 6;
    public const int TwelveHours = 12;
    public const int DefaultHours = 24;
    public const int ThreeDays = 72;

    public static readonly IReadOnlyList<int> Options = new[] { None, SixHours, TwelveHours, DefaultHours, ThreeDays };
}
