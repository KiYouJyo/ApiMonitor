namespace ApiMonitor.Models;

/// <summary>
/// 每个规则（AccountId + MetricId）独立维护的通知状态。
/// 阈值状态机与 Windows API 调用分离，便于单元测试。
/// </summary>
public enum NotificationStateKind
{
    Unknown,
    Normal,
    Low,
    Snoozed,
}

public sealed class NotificationStateEntry
{
    public required string AccountId { get; init; }

    public required string MetricId { get; init; }

    /// <summary>最近一次参与评估的快照 ID（同一快照去重）。</summary>
    public string? LastEvaluatedSnapshotId { get; set; }

    public NotificationStateKind LastState { get; set; } = NotificationStateKind.Unknown;

    public DateTimeOffset? LastNotifiedAt { get; set; }

    public DateTimeOffset? LastRecoveryNotifiedAt { get; set; }

    public DateTimeOffset? SnoozedUntil { get; set; }

    public string? LastNotificationTag { get; set; }
}
