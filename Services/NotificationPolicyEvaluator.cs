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
    bool NotificationsSuppressed,
    IReadOnlyList<HealthNotificationItem>? HealthItems = null,
    bool ShouldNotifyHealth = false);

/// <summary>v0.9.0：服务健康通知类型。</summary>
public enum HealthNotificationType
{
    CredentialInvalid,
    PermissionDenied,
    ServiceNotEnabled,
    QuotaExceeded,
    ServiceUnavailable,
    ServiceRecovered,
    ExpectedServiceMissing,
    ExpectedServiceRecovered,
}

/// <summary>v0.9.0：服务健康通知条目（只含安全类别与展示文本，不含密钥/URL）。</summary>
public sealed record HealthNotificationItem(
    string AccountId,
    string ProviderId,
    HealthNotificationType Type,
    string Message);

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

    /// <summary>
    /// v0.9.0：探测失败（无快照）时对地理/GIS 服务账户的健康通知评估。
    /// 明确的凭据/权限/配额错误首次出现即可通知；
    /// 网络超时/TLS/5xx 等瞬时错误至少连续两次后才通知。
    /// </summary>
    NotificationDecision EvaluateFailure(
        ApiAccount account,
        BalanceQueryError error,
        NotificationGlobalSettings globalSettings,
        IReadOnlyList<NotificationStateEntry> currentStates,
        DateTimeOffset nowUtc);
}

public sealed class NotificationPolicyEvaluator : INotificationPolicyEvaluator
{
    /// <summary>服务健康状态条目的稳定 MetricId（账户级单条状态）。</summary>
    public const string HealthStateMetricId = "service.availability";

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
                NotificationsSuppressed: true,
                HealthItems: Array.Empty<HealthNotificationItem>());
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
        var healthItems = new List<HealthNotificationItem>();
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

        EvaluateHealthOnSnapshot(
            account,
            snapshot,
            stateByKey,
            updated,
            updatedIndex,
            healthItems,
            recoveryEnabled,
            nowUtc);

        return new NotificationDecision(
            ShouldNotifyLow: lowItems.Count > 0,
            ShouldNotifyRecovery: recoveryItems.Count > 0,
            LowItems: lowItems,
            RecoveryItems: recoveryItems,
            UpdatedStates: updated,
            NotificationsSuppressed: false,
            HealthItems: healthItems,
            ShouldNotifyHealth: healthItems.Count > 0);
    }

    public NotificationDecision EvaluateFailure(
        ApiAccount account,
        BalanceQueryError error,
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
                NotificationsSuppressed: true,
                HealthItems: Array.Empty<HealthNotificationItem>());
        }

        var stateByKey = currentStates.ToDictionary(
            s => Key(s.AccountId, s.MetricId),
            StringComparer.OrdinalIgnoreCase);
        var updated = new List<NotificationStateEntry>(currentStates.Count);
        var updatedIndex = new Dictionary<string, NotificationStateEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in currentStates)
        {
            var clone = CloneState(existing);
            updated.Add(clone);
            updatedIndex[Key(clone.AccountId, clone.MetricId)] = clone;
        }

        var healthItems = new List<HealthNotificationItem>();
        EvaluateHealthOnFailure(
            account,
            error,
            stateByKey,
            updated,
            updatedIndex,
            healthItems,
            nowUtc);

        return new NotificationDecision(
            ShouldNotifyLow: false,
            ShouldNotifyRecovery: false,
            LowItems: Array.Empty<LowBalanceNotificationItem>(),
            RecoveryItems: Array.Empty<RecoveryNotificationItem>(),
            UpdatedStates: updated,
            NotificationsSuppressed: false,
            HealthItems: healthItems,
            ShouldNotifyHealth: healthItems.Count > 0);
    }

    /// <summary>
    /// 成功快照中的服务健康评估（地理/GIS Provider 的 service.availability 指标）。
    /// 规则：
    ///   - 明确的凭据/权限/配额状态变化首次出现即通知；
    ///   - 网络/TLS/ProviderError 等瞬时状态至少连续两次后通知；
    ///   - 预期服务/图层缺失首次出现即通知；
    ///   - 恢复成功一次后发送恢复通知。
    /// </summary>
    private static void EvaluateHealthOnSnapshot(
        ApiAccount account,
        BalanceSnapshot snapshot,
        IReadOnlyDictionary<string, NotificationStateEntry> stateByKey,
        List<NotificationStateEntry> updated,
        Dictionary<string, NotificationStateEntry> updatedIndex,
        List<HealthNotificationItem> healthItems,
        bool recoveryEnabled,
        DateTimeOffset nowUtc)
    {
        var availability = snapshot.Metrics.FirstOrDefault(m =>
            m.DetailedKind == MetricKind.ServiceAvailability);
        if (availability is null)
        {
            return;
        }

        var status = GeospatialMetricFactory.Parse(availability.StatusValue);
        bool expectedMissing = snapshot.Metrics.Any(m =>
            (m.MetricId.EndsWith("expected-service.present", StringComparison.OrdinalIgnoreCase)
                || m.MetricId.EndsWith("expected-layer.present", StringComparison.OrdinalIgnoreCase))
            && m.BooleanValue == false);

        string key = Key(account.AccountId, HealthStateMetricId);
        var previous = stateByKey.TryGetValue(key, out var existing) ? existing : null;
        if (!updatedIndex.TryGetValue(key, out var state))
        {
            state = new NotificationStateEntry
            {
                AccountId = account.AccountId,
                MetricId = HealthStateMetricId,
            };
            updated.Add(state);
            updatedIndex[key] = state;
        }

        state.LastEvaluatedSnapshotId = snapshot.SnapshotId;
        bool snoozed = state.SnoozedUntil is { } until && until > nowUtc;

        if (expectedMissing)
        {
            state.LastStatusValue = "expected-missing";
            state.ConsecutiveFailures = previous?.LastStatusValue == "expected-missing"
                ? (previous.ConsecutiveFailures + 1)
                : 1;
            if (!snoozed && previous?.LastStatusValue != "expected-missing")
            {
                healthItems.Add(new HealthNotificationItem(
                    account.AccountId,
                    account.ProviderId,
                    HealthNotificationType.ExpectedServiceMissing,
                    L10n.Get("Notification.ExpectedServiceMissing")));
            }

            return;
        }

        if (status is GeospatialStatus.Healthy or GeospatialStatus.Unknown)
        {
            // 恢复：上一状态是问题状态，恢复成功一次即发送恢复通知。
            bool wasProblem = previous?.LastStatusValue is { } prev
                && prev != nameof(GeospatialStatus.Healthy)
                && prev != nameof(GeospatialStatus.Unknown);
            if (wasProblem && recoveryEnabled)
            {
                healthItems.Add(new HealthNotificationItem(
                    account.AccountId,
                    account.ProviderId,
                    HealthNotificationType.ServiceRecovered,
                    L10n.Get("Notification.ServiceRecovered")));
            }

            state.LastStatusValue = status.ToString();
            state.ConsecutiveFailures = 0;
            state.SnoozedUntil = null;
            return;
        }

        bool deterministic = IsDeterministic(status);
        string problemKey = status.ToString();
        state.LastStatusValue = problemKey;
        state.ConsecutiveFailures = previous?.LastStatusValue == problemKey
            ? previous.ConsecutiveFailures + 1
            : 1;
        state.SnoozedUntil = null;

        bool notify = !snoozed
            && (deterministic
                ? previous?.LastStatusValue != problemKey
                : state.ConsecutiveFailures >= 2);
        if (notify)
        {
            healthItems.Add(new HealthNotificationItem(
                account.AccountId,
                account.ProviderId,
                MapSuccessStatusToType(status),
                GeospatialMetricFactory.StatusText(status)));
        }
    }

    /// <summary>探测失败（无快照）时的健康状态与通知评估。</summary>
    private static void EvaluateHealthOnFailure(
        ApiAccount account,
        BalanceQueryError error,
        IReadOnlyDictionary<string, NotificationStateEntry> stateByKey,
        List<NotificationStateEntry> updated,
        Dictionary<string, NotificationStateEntry> updatedIndex,
        List<HealthNotificationItem> healthItems,
        DateTimeOffset nowUtc)
    {
        var type = MapFailureToType(error.Kind);
        bool deterministic = type is not HealthNotificationType.ServiceUnavailable;

        string key = Key(account.AccountId, HealthStateMetricId);
        var previous = stateByKey.TryGetValue(key, out var existing) ? existing : null;
        if (!updatedIndex.TryGetValue(key, out var state))
        {
            state = new NotificationStateEntry
            {
                AccountId = account.AccountId,
                MetricId = HealthStateMetricId,
            };
            updated.Add(state);
            updatedIndex[key] = state;
        }

        string problemKey = type.ToString();
        state.LastStatusValue = problemKey;
        state.ConsecutiveFailures = previous?.LastStatusValue == problemKey
            ? previous.ConsecutiveFailures + 1
            : 1;

        bool snoozed = state.SnoozedUntil is { } until && until > nowUtc;
        bool notify = !snoozed
            && (deterministic
                ? previous?.LastStatusValue != problemKey
                : state.ConsecutiveFailures >= 2);
        if (notify)
        {
            healthItems.Add(new HealthNotificationItem(
                account.AccountId,
                account.ProviderId,
                type,
                FailureMessage(error)));
        }
    }

    private static bool IsDeterministic(GeospatialStatus status) =>
        status is GeospatialStatus.CredentialInvalid
            or GeospatialStatus.KeyTypeMismatch
            or GeospatialStatus.IpWhitelistDenied
            or GeospatialStatus.RefererDomainDenied
            or GeospatialStatus.SignatureInvalid
            or GeospatialStatus.PermissionDenied
            or GeospatialStatus.ServiceNotEnabled
            or GeospatialStatus.QuotaExceeded
            or GeospatialStatus.RateLimited
            or GeospatialStatus.ConfigurationMissing;

    private static HealthNotificationType MapSuccessStatusToType(GeospatialStatus status) =>
        status switch
        {
            GeospatialStatus.CredentialInvalid or GeospatialStatus.KeyTypeMismatch
                or GeospatialStatus.SignatureInvalid
                => HealthNotificationType.CredentialInvalid,
            GeospatialStatus.PermissionDenied or GeospatialStatus.IpWhitelistDenied
                or GeospatialStatus.RefererDomainDenied
                => HealthNotificationType.PermissionDenied,
            GeospatialStatus.ServiceNotEnabled => HealthNotificationType.ServiceNotEnabled,
            GeospatialStatus.QuotaExceeded or GeospatialStatus.RateLimited
                => HealthNotificationType.QuotaExceeded,
            _ => HealthNotificationType.ServiceUnavailable,
        };

    private static HealthNotificationType MapFailureToType(BalanceErrorKind kind) =>
        kind switch
        {
            BalanceErrorKind.CredentialInvalid or BalanceErrorKind.KeyTypeMismatch
                or BalanceErrorKind.SignatureInvalid or BalanceErrorKind.Unauthorized
                => HealthNotificationType.CredentialInvalid,
            BalanceErrorKind.PermissionDenied or BalanceErrorKind.IpWhitelistDenied
                or BalanceErrorKind.RefererDomainDenied or BalanceErrorKind.Forbidden
                => HealthNotificationType.PermissionDenied,
            BalanceErrorKind.ServiceNotEnabled => HealthNotificationType.ServiceNotEnabled,
            BalanceErrorKind.QuotaExceeded or BalanceErrorKind.PaymentRequired
                => HealthNotificationType.QuotaExceeded,
            BalanceErrorKind.ExpectedServiceMissing or BalanceErrorKind.ExpectedLayerMissing
                => HealthNotificationType.ExpectedServiceMissing,
            _ => HealthNotificationType.ServiceUnavailable,
        };

    private static string FailureMessage(BalanceQueryError error) =>
        error.HttpStatusCode is { } http
            ? L10n.Format("Notification.ServiceUnavailableWithStatus", error.Message, http)
            : error.Message;

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
            LastStatusValue = source.LastStatusValue,
            ConsecutiveFailures = source.ConsecutiveFailures,
        };

    private static string Key(string accountId, string metricId) =>
        accountId + "\u0000" + metricId;
}
