using ApiMonitor.Models;
using ApiMonitor.Helpers;

namespace ApiMonitor.Services;

/// <summary>
/// 通知激活参数（只包含非敏感账户标识；绝不包含 API Key、
/// 余额正文、Authorization、Credential Locker Resource 或本机路径）。
/// </summary>
public sealed record NotificationActivationPayload(
    string Action,
    string? AccountId,
    string? ProviderId,
    string? MetricId)
{
    public static NotificationActivationPayload Empty { get; } = new(string.Empty, null, null, null);

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Action)
        && (Action == NotificationActions.Test || !string.IsNullOrWhiteSpace(AccountId));
}

/// <summary>通知动作常量（与通知参数中的 action 对应）。</summary>
public static class NotificationActions
{
    public const string OpenAccount = "open";
    public const string Snooze24Hours = "snooze";
    public const string Test = "test";
}

/// <summary>通知 Tag/Group 常量（稳定 Tag 替换同账户旧提醒）。</summary>
public static class NotificationTags
{
    public const string Group = "ApiMonitor";

    public static string AccountTag(string accountId) => "ApiMonitor-" + accountId;
}

/// <summary>低余额通知中的单个指标条目。</summary>
public sealed record LowBalanceNotificationItem(
    string MetricId,
    string DisplayName,
    decimal Value,
    decimal Threshold,
    string ValueText);

/// <summary>恢复通知中的单个指标条目。</summary>
public sealed record RecoveryNotificationItem(
    string MetricId,
    string DisplayName,
    decimal Value,
    string ValueText);

/// <summary>一次评估的结果：需要发送的通知与更新后的状态集合。</summary>
public sealed record NotificationDecision(
    bool ShouldNotifyLow,
    bool ShouldNotifyRecovery,
    IReadOnlyList<LowBalanceNotificationItem> LowItems,
    IReadOnlyList<RecoveryNotificationItem> RecoveryItems,
    IReadOnlyList<NotificationStateEntry> UpdatedStates,
    bool NotificationsSuppressed);

/// <summary>
/// 低余额通知策略评估器（纯逻辑，不调用任何 Windows API）。
/// 通知只能由成功查询产生的新快照触发；查询失败、阈值保存本身、
/// 启动时的旧快照都不会触发。
/// </summary>
public interface INotificationPolicyEvaluator
{
    NotificationDecision Evaluate(
        ApiAccount account,
        BalanceSnapshot snapshot,
        NotificationGlobalSettings globalSettings,
        IReadOnlyList<NotificationStateEntry> currentStates,
        DateTimeOffset nowUtc);
}

public sealed class NotificationPolicyEvaluator : INotificationPolicyEvaluator
{
    public NotificationDecision Evaluate(
        ApiAccount account,
        BalanceSnapshot snapshot,
        NotificationGlobalSettings globalSettings,
        IReadOnlyList<NotificationStateEntry> currentStates,
        DateTimeOffset nowUtc)
    {
        bool accountEnabled = account.Notification.NotificationsEnabled ?? true;
        bool globallyEnabled = globalSettings.BalanceNotificationsEnabled;
        bool enabled = globallyEnabled && accountEnabled;

        if (!enabled)
        {
            return new NotificationDecision(
                ShouldNotifyLow: false,
                ShouldNotifyRecovery: false,
                LowItems: Array.Empty<LowBalanceNotificationItem>(),
                RecoveryItems: Array.Empty<RecoveryNotificationItem>(),
                UpdatedStates: currentStates,
                NotificationsSuppressed: true);
        }

        var stateByKey = currentStates.ToDictionary(
            s => Key(s.AccountId, s.MetricId),
            StringComparer.OrdinalIgnoreCase);

        int repeatIntervalHours = account.Notification.RepeatIntervalHours
            ?? globalSettings.DefaultRepeatIntervalHours;
        bool recoveryEnabled = account.Notification.RecoveryNotificationsEnabled
            ?? globalSettings.RecoveryNotificationsEnabled;

        var lowItems = new List<LowBalanceNotificationItem>();
        var recoveryItems = new List<RecoveryNotificationItem>();
        var updated = new List<NotificationStateEntry>(currentStates.Count);
        var updatedIndex = new Dictionary<string, NotificationStateEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in currentStates)
        {
            var clone = CloneState(existing);
            updated.Add(clone);
            updatedIndex[Key(clone.AccountId, clone.MetricId)] = clone;
        }

        foreach (var rule in account.Monitoring.Thresholds.Where(r => r.IsEnabled))
        {
            var metric = snapshot.Metrics.FirstOrDefault(m =>
                string.Equals(m.MetricId, rule.MetricId, StringComparison.OrdinalIgnoreCase));
            if (metric is null || !metric.IsThresholdSupported)
            {
                continue;
            }

            string key = Key(account.AccountId, rule.MetricId);
            var previous = stateByKey.TryGetValue(key, out var existing) ? existing : null;

            // 同一快照已评估过（手动/自动刷新、多窗口事件、重复订阅）→ 去重跳过。
            if (previous?.LastEvaluatedSnapshotId is { } lastId
                && string.Equals(lastId, snapshot.SnapshotId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!updatedIndex.TryGetValue(key, out var state))
            {
                state = new NotificationStateEntry
                {
                    AccountId = account.AccountId,
                    MetricId = rule.MetricId,
                };
                updated.Add(state);
                updatedIndex[key] = state;
            }

            state.LastEvaluatedSnapshotId = snapshot.SnapshotId;

            var status = ThresholdEvaluator.Evaluate(metric, rule);
            bool snoozed = state.SnoozedUntil is { } until && until > nowUtc;

            if (status == ThresholdStatus.BelowThreshold)
            {
                if (snoozed)
                {
                    state.LastState = NotificationStateKind.Snoozed;
                }
                else
                {
                    state.LastState = NotificationStateKind.Low;
                    state.SnoozedUntil = null;
                    if (previous?.LastState is NotificationStateKind.Low or NotificationStateKind.Snoozed)
                    {
                        // 持续低余额：只有超过重复提醒间隔才再次通知（0 = 不重复）。
                        if (repeatIntervalHours > 0
                            && (state.LastNotifiedAt is null
                                || nowUtc - state.LastNotifiedAt.Value >= TimeSpan.FromHours(repeatIntervalHours)))
                        {
                            lowItems.Add(BuildLowItem(metric, rule));
                            state.LastNotifiedAt = nowUtc;
                        }
                    }
                    else
                    {
                        // 首次低余额：Normal/Unknown → Low，发送一次通知。
                        lowItems.Add(BuildLowItem(metric, rule));
                        state.LastNotifiedAt = nowUtc;
                    }
                }
            }
            else if (status == ThresholdStatus.Normal)
            {
                if (previous?.LastState is NotificationStateKind.Low or NotificationStateKind.Snoozed)
                {
                    // Low → Normal：按设置发送一次恢复通知。
                    if (recoveryEnabled && BalanceMetricText.MainAmount(metric) is { } value)
                    {
                        recoveryItems.Add(new RecoveryNotificationItem(
                            metric.MetricId,
                            metric.DisplayName,
                            value,
                            BalanceMetricText.BuildRecoveryValueText(metric)));
                        state.LastRecoveryNotifiedAt = nowUtc;
                    }
                }

                state.LastState = NotificationStateKind.Normal;
                state.SnoozedUntil = null;
            }
            else
            {
                // 数值未知或指标不支持阈值：保留原状态，不触发任何通知。
                continue;
            }

        }

        return new NotificationDecision(
            ShouldNotifyLow: lowItems.Count > 0,
            ShouldNotifyRecovery: recoveryItems.Count > 0,
            LowItems: lowItems,
            RecoveryItems: recoveryItems,
            UpdatedStates: updated,
            NotificationsSuppressed: false);
    }

    private static LowBalanceNotificationItem BuildLowItem(BalanceMetric metric, BalanceThresholdRule rule)
    {
        decimal value = BalanceMetricText.MainAmount(metric) ?? rule.ThresholdAmount;
        return new LowBalanceNotificationItem(
            metric.MetricId,
            metric.DisplayName,
            value,
            rule.ThresholdAmount,
            BalanceMetricText.BuildLowBalanceValueText(metric, rule.ThresholdAmount));
    }

    private static NotificationStateEntry CloneState(NotificationStateEntry source) =>
        new()
        {
            AccountId = source.AccountId,
            MetricId = source.MetricId,
            LastEvaluatedSnapshotId = source.LastEvaluatedSnapshotId,
            LastState = source.LastState,
            LastNotifiedAt = source.LastNotifiedAt,
            LastRecoveryNotifiedAt = source.LastRecoveryNotifiedAt,
            SnoozedUntil = source.SnoozedUntil,
            LastNotificationTag = source.LastNotificationTag,
        };

    private static string Key(string accountId, string metricId) =>
        accountId + "\u0000" + metricId;
}
