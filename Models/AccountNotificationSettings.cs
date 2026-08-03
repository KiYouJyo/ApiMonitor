namespace ApiMonitor.Models;

/// <summary>
/// 每账户通知设置（v0.5.0）。null 字段表示继承全局通知设置；
/// 阈值规则本身仍保存在 <see cref="MonitoringSettings.Thresholds"/>，
/// 通知引擎不建立第二套阈值计算。
/// </summary>
public sealed class AccountNotificationSettings
{
    /// <summary>是否启用该账户通知；null = 继承全局。</summary>
    public bool? NotificationsEnabled { get; set; }

    /// <summary>重复提醒间隔（小时）；null = 继承全局；0 = 不重复。</summary>
    public int? RepeatIntervalHours { get; set; }

    /// <summary>余额恢复提醒开关；null = 继承全局。</summary>
    public bool? RecoveryNotificationsEnabled { get; set; }
}
