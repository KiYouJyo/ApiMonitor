using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>账户数据文件（accounts.json）的序列化模型。</summary>
public sealed class AccountsFileData
{
    public int SchemaVersion { get; set; } = JsonAccountStore.CurrentSchemaVersion;

    public List<AccountFileEntry> Accounts { get; set; } = new();
}

public sealed class AccountFileEntry
{
    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool HasCredential { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>v0.2.0 起持久化；v0.1.0 文件迁移后写入默认值。</summary>
    public MonitoringFileEntry? Monitoring { get; set; }

    /// <summary>v0.5.0：Provider 专属的非敏感设置（如 OpenRouter 凭据模式）。</summary>
    public string? CredentialMode { get; set; }

    /// <summary>
    /// v0.8.0：Provider 专属的非敏感配置字段（如 xAI Team ID）。
    /// 可空：v0.7.0 及更早文件加载时自然得到 null，无需升级 schemaVersion。
    /// 密钥绝不保存在这里。
    /// </summary>
    public Dictionary<string, string>? ProviderConfig { get; set; }

    /// <summary>
    /// v0.9.0：多字段凭据存在状态（slot → 是否存在）。
    /// 可空：v0.8.0 及更早文件加载时自然得到 null。
    /// 只保存存在标志，绝不保存凭据值。
    /// </summary>
    public Dictionary<string, bool>? CredentialSlots { get; set; }

    /// <summary>v0.5.0：每账户通知设置（null 表示继承全局设置）。</summary>
    public AccountNotificationFileEntry? Notification { get; set; }
}

public sealed class MonitoringFileEntry
{
    public bool AutoRefreshEnabled { get; set; } = true;

    public int RefreshIntervalMinutes { get; set; } = MonitoringIntervals.DefaultMinutes;

    public DateTimeOffset? NextRefreshAtUtc { get; set; }

    public List<ThresholdFileEntry> Thresholds { get; set; } = new();
}

public sealed class ThresholdFileEntry
{
    public string MetricId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public decimal ThresholdAmount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>每账户通知设置（v0.5.0）。null 字段表示继承全局通知设置。</summary>
public sealed class AccountNotificationFileEntry
{
    public bool? NotificationsEnabled { get; set; }

    /// <summary>重复提醒间隔（小时）；null 继承全局；0 表示不重复。</summary>
    public int? RepeatIntervalHours { get; set; }

    public bool? RecoveryNotificationsEnabled { get; set; }
}

/// <summary>余额记录数据文件（balance-records.json）的序列化模型。</summary>
public sealed class BalanceRecordsFileData
{
    public int SchemaVersion { get; set; } = JsonBalanceSnapshotStore.CurrentSchemaVersion;

    public List<BalanceRecordFileEntry> Records { get; set; } = new();
}

public sealed class BalanceRecordFileEntry
{
    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public DateTimeOffset? LastQueryAttemptAt { get; set; }

    public DateTimeOffset? LastQuerySuccessAt { get; set; }

    public SnapshotFileEntry? LastSuccessfulSnapshot { get; set; }

    /// <summary>v0.2.0 起的余额历史（按时间倒序）。</summary>
    public List<HistoryFileEntry> History { get; set; } = new();
}

public sealed class SnapshotFileEntry
{
    public string SnapshotId { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public DateTimeOffset RetrievedAt { get; set; }

    public List<BalanceMetricFileEntry> Metrics { get; set; } = new();
}

public sealed class HistoryFileEntry
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public DateTimeOffset SucceededAtUtc { get; set; }

    public string Source { get; set; } = nameof(BalanceQuerySource.Manual);

    public bool IsAvailable { get; set; }

    public List<BalanceMetricFileEntry> Metrics { get; set; } = new();
}

public sealed class BalanceMetricFileEntry
{
    public string MetricId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Kind { get; set; } = nameof(BalanceMetricKind.Other);

    public decimal? AvailableAmount { get; set; }

    public decimal? TotalAmount { get; set; }

    public decimal? UsedAmount { get; set; }

    public decimal? GrantedAmount { get; set; }

    public decimal? ToppedUpAmount { get; set; }

    public bool IsThresholdSupported { get; set; }

    public bool IsUnlimited { get; set; }

    public List<BalanceMetricAdditionalFileEntry> AdditionalDisplayValues { get; set; } = new();

    /// <summary>v0.9.0：指标值类型（旧文件缺省为 Decimal）。</summary>
    public string ValueKind { get; set; } = nameof(MetricValueKind.Decimal);

    /// <summary>v0.9.0：指标详细类型（旧 AI 指标为 null）。</summary>
    public string? DetailedKind { get; set; }

    /// <summary>v0.9.0：状态值（ValueKind=Status）。</summary>
    public string? StatusValue { get; set; }

    /// <summary>v0.9.0：布尔值（ValueKind=Boolean）。</summary>
    public bool? BooleanValue { get; set; }

    /// <summary>v0.9.0：整数/计数/延迟值（ValueKind=Integer）。</summary>
    public long? IntegerValue { get; set; }

    /// <summary>v0.9.0：时间戳值（ValueKind=Timestamp）。</summary>
    public DateTimeOffset? TimestampValue { get; set; }
}

public sealed class BalanceMetricAdditionalFileEntry
{
    public string Name { get; set; } = string.Empty;

    public decimal Value { get; set; }

    public string? Unit { get; set; }
}

// ---------------------------------------------------------------------------
// v0.4.0（schemaVersion 1/2）旧版 DTO：仅用于读取旧文件后一次性迁移到
// 通用 BalanceMetric 结构，绝不用于新文件写入。
// ---------------------------------------------------------------------------

public sealed class LegacyAccountsFileData
{
    public int SchemaVersion { get; set; }

    public List<LegacyAccountFileEntry> Accounts { get; set; } = new();
}

public sealed class LegacyAccountFileEntry
{
    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool HasCredential { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public LegacyMonitoringFileEntry? Monitoring { get; set; }
}

public sealed class LegacyMonitoringFileEntry
{
    public bool AutoRefreshEnabled { get; set; } = true;

    public int RefreshIntervalMinutes { get; set; } = MonitoringIntervals.DefaultMinutes;

    public DateTimeOffset? NextRefreshAtUtc { get; set; }

    public List<LegacyThresholdFileEntry> Thresholds { get; set; } = new();
}

public sealed class LegacyThresholdFileEntry
{
    public string Currency { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public decimal ThresholdAmount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class LegacyBalanceRecordsFileData
{
    public int SchemaVersion { get; set; }

    public List<LegacyBalanceRecordFileEntry> Records { get; set; } = new();
}

public sealed class LegacyBalanceRecordFileEntry
{
    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public DateTimeOffset? LastQueryAttemptAt { get; set; }

    public DateTimeOffset? LastQuerySuccessAt { get; set; }

    public LegacySnapshotFileEntry? LastSuccessfulSnapshot { get; set; }

    public List<LegacyHistoryFileEntry> History { get; set; } = new();
}

public sealed class LegacySnapshotFileEntry
{
    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public DateTimeOffset RetrievedAt { get; set; }

    public List<LegacyBalanceAmountFileEntry> Balances { get; set; } = new();
}

public sealed class LegacyHistoryFileEntry
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public DateTimeOffset SucceededAtUtc { get; set; }

    public string Source { get; set; } = nameof(BalanceQuerySource.Manual);

    public bool IsAvailable { get; set; }

    public List<LegacyBalanceAmountFileEntry> Balances { get; set; } = new();
}

public sealed class LegacyBalanceAmountFileEntry
{
    public string Currency { get; set; } = string.Empty;

    public decimal TotalBalance { get; set; }

    public decimal GrantedBalance { get; set; }

    public decimal ToppedUpBalance { get; set; }
}

/// <summary>DeepSeek 货币余额指标 ID 前缀（v0.4.0 → v0.5.0 迁移与查询共用）。</summary>
internal static class BalanceMetricIds
{
    public static string DeepSeekCurrencyTotal(string currency) =>
        $"deepseek:{currency}:total";

    public static string DeepSeekCurrencyDisplayName(string currency) =>
        $"{currency} 总余额";

    public static BalanceMetric ToMetric(LegacyBalanceAmountFileEntry entry)
    {
        string currency = entry.Currency.Trim();
        return new BalanceMetric
        {
            MetricId = DeepSeekCurrencyTotal(currency),
            DisplayName = DeepSeekCurrencyDisplayName(currency),
            Unit = currency,
            Kind = BalanceMetricKind.MonetaryBalance,
            AvailableAmount = entry.TotalBalance,
            TotalAmount = entry.TotalBalance,
            GrantedAmount = entry.GrantedBalance,
            ToppedUpAmount = entry.ToppedUpBalance,
            IsThresholdSupported = true,
        };
    }
}

internal static class StorageMapper
{
    public static ApiAccount ToAccount(AccountFileEntry entry)
    {
        MonitoringSettings monitoring = entry.Monitoring is { } m
            ? new MonitoringSettings
            {
                AutoRefreshEnabled = m.AutoRefreshEnabled,
                RefreshIntervalMinutes = m.RefreshIntervalMinutes,
                NextRefreshAtUtc = m.NextRefreshAtUtc,
                Thresholds = m.Thresholds
                    .Where(t => !string.IsNullOrWhiteSpace(t.MetricId))
                    .Select(t => new BalanceThresholdRule
                    {
                        MetricId = t.MetricId,
                        DisplayName = t.DisplayName,
                        Unit = t.Unit,
                        IsEnabled = t.IsEnabled,
                        ThresholdAmount = t.ThresholdAmount,
                        CreatedAtUtc = t.CreatedAtUtc,
                        UpdatedAtUtc = t.UpdatedAtUtc,
                    })
                    .ToList(),
            }
            : new MonitoringSettings();

        AccountNotificationSettings notification = entry.Notification is { } n
            ? new AccountNotificationSettings
            {
                NotificationsEnabled = n.NotificationsEnabled,
                RepeatIntervalHours = n.RepeatIntervalHours,
                RecoveryNotificationsEnabled = n.RecoveryNotificationsEnabled,
            }
            : new AccountNotificationSettings();

        return new ApiAccount
        {
            AccountId = entry.AccountId,
            ProviderId = entry.ProviderId,
            DisplayName = entry.DisplayName,
            HasCredential = entry.HasCredential,
            CreatedAtUtc = entry.CreatedAtUtc,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            Monitoring = monitoring,
            CredentialMode = entry.CredentialMode,
            ProviderConfig = entry.ProviderConfig is { } config
                ? new Dictionary<string, string>(config, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal),
            CredentialSlots = entry.CredentialSlots is { } slots
                ? new Dictionary<string, bool>(slots, StringComparer.Ordinal)
                : new Dictionary<string, bool>(StringComparer.Ordinal),
            Notification = notification,
        };
    }

    public static AccountFileEntry ToEntry(ApiAccount account) =>
        new()
        {
            AccountId = account.AccountId,
            ProviderId = account.ProviderId,
            DisplayName = account.DisplayName,
            HasCredential = account.HasCredential,
            CreatedAtUtc = account.CreatedAtUtc,
            UpdatedAtUtc = account.UpdatedAtUtc,
            CredentialMode = account.CredentialMode,
            ProviderConfig = account.ProviderConfig is { Count: > 0 }
                ? new Dictionary<string, string>(account.ProviderConfig, StringComparer.Ordinal)
                : null,
            CredentialSlots = account.CredentialSlots is { Count: > 0 }
                ? new Dictionary<string, bool>(account.CredentialSlots, StringComparer.Ordinal)
                : null,
            Notification = account.Notification is { } n
                ? new AccountNotificationFileEntry
                {
                    NotificationsEnabled = n.NotificationsEnabled,
                    RepeatIntervalHours = n.RepeatIntervalHours,
                    RecoveryNotificationsEnabled = n.RecoveryNotificationsEnabled,
                }
                : null,
            Monitoring = new MonitoringFileEntry
            {
                AutoRefreshEnabled = account.Monitoring.AutoRefreshEnabled,
                RefreshIntervalMinutes = account.Monitoring.RefreshIntervalMinutes,
                NextRefreshAtUtc = account.Monitoring.NextRefreshAtUtc,
                Thresholds = account.Monitoring.Thresholds
                    .Select(t => new ThresholdFileEntry
                    {
                        MetricId = t.MetricId,
                        DisplayName = t.DisplayName,
                        Unit = t.Unit,
                        IsEnabled = t.IsEnabled,
                        ThresholdAmount = t.ThresholdAmount,
                        CreatedAtUtc = t.CreatedAtUtc,
                        UpdatedAtUtc = t.UpdatedAtUtc,
                    })
                    .ToList(),
            },
        };

    public static AccountBalanceRecord ToRecord(BalanceRecordFileEntry entry) =>
        new()
        {
            AccountId = entry.AccountId,
            ProviderId = entry.ProviderId,
            LastQueryAttemptAt = entry.LastQueryAttemptAt,
            LastQuerySuccessAt = entry.LastQuerySuccessAt,
            LastSuccessfulSnapshot = entry.LastSuccessfulSnapshot is { } snapshot
                ? ToSnapshot(snapshot)
                : null,
            History = entry.History
                .Where(h => !string.IsNullOrWhiteSpace(h.Id))
                .Select(ToHistoryEntry)
                .OrderByDescending(h => h.SucceededAtUtc)
                .ThenBy(h => h.Id)
                .ToList(),
        };

    public static BalanceRecordFileEntry ToEntry(AccountBalanceRecord record) =>
        new()
        {
            AccountId = record.AccountId,
            ProviderId = record.ProviderId,
            LastQueryAttemptAt = record.LastQueryAttemptAt,
            LastQuerySuccessAt = record.LastQuerySuccessAt,
            LastSuccessfulSnapshot = record.LastSuccessfulSnapshot is { } snapshot
                ? ToSnapshotEntry(snapshot)
                : null,
            History = record.History
                .OrderByDescending(h => h.SucceededAtUtc)
                .ThenBy(h => h.Id)
                .Select(ToHistoryFileEntry)
                .ToList(),
        };

    public static BalanceSnapshot ToSnapshot(SnapshotFileEntry entry) =>
        new()
        {
            SnapshotId = string.IsNullOrWhiteSpace(entry.SnapshotId)
                ? Guid.NewGuid().ToString("N")
                : entry.SnapshotId,
            AccountId = entry.AccountId,
            ProviderId = entry.ProviderId,
            IsAvailable = entry.IsAvailable,
            RetrievedAt = entry.RetrievedAt,
            Metrics = entry.Metrics
                .Where(m => !string.IsNullOrWhiteSpace(m.MetricId))
                .Select(ToBalanceMetric)
                .ToList(),
        };

    public static SnapshotFileEntry ToSnapshotEntry(BalanceSnapshot snapshot) =>
        new()
        {
            SnapshotId = snapshot.SnapshotId,
            AccountId = snapshot.AccountId,
            ProviderId = snapshot.ProviderId,
            IsAvailable = snapshot.IsAvailable,
            RetrievedAt = snapshot.RetrievedAt,
            Metrics = snapshot.Metrics
                .Select(ToBalanceMetricFileEntry)
                .ToList(),
        };

    public static BalanceHistoryEntry ToHistoryEntry(HistoryFileEntry entry) =>
        new()
        {
            Id = entry.Id,
            AccountId = entry.AccountId,
            ProviderId = entry.ProviderId,
            SucceededAtUtc = entry.SucceededAtUtc,
            Source = Enum.TryParse<BalanceQuerySource>(entry.Source, ignoreCase: true, out var source)
                ? source
                : BalanceQuerySource.Manual,
            IsAvailable = entry.IsAvailable,
            Metrics = entry.Metrics
                .Where(m => !string.IsNullOrWhiteSpace(m.MetricId))
                .Select(ToBalanceMetric)
                .ToList(),
        };

    public static HistoryFileEntry ToHistoryFileEntry(BalanceHistoryEntry entry) =>
        new()
        {
            Id = entry.Id,
            AccountId = entry.AccountId,
            ProviderId = entry.ProviderId,
            SucceededAtUtc = entry.SucceededAtUtc,
            Source = entry.Source.ToString(),
            IsAvailable = entry.IsAvailable,
            Metrics = entry.Metrics.Select(ToBalanceMetricFileEntry).ToList(),
        };

    public static BalanceMetric ToBalanceMetric(BalanceMetricFileEntry entry) =>
        new()
        {
            MetricId = entry.MetricId,
            DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? entry.MetricId
                : entry.DisplayName,
            Unit = entry.Unit,
            Kind = Enum.TryParse<BalanceMetricKind>(entry.Kind, ignoreCase: true, out var kind)
                ? kind
                : BalanceMetricKind.Other,
            AvailableAmount = entry.AvailableAmount,
            TotalAmount = entry.TotalAmount,
            UsedAmount = entry.UsedAmount,
            GrantedAmount = entry.GrantedAmount,
            ToppedUpAmount = entry.ToppedUpAmount,
            IsThresholdSupported = entry.IsThresholdSupported,
            IsUnlimited = entry.IsUnlimited,
            ValueKind = Enum.TryParse<MetricValueKind>(entry.ValueKind, ignoreCase: true, out var valueKind)
                ? valueKind
                : MetricValueKind.Decimal,
            DetailedKind = !string.IsNullOrWhiteSpace(entry.DetailedKind)
                && Enum.TryParse<MetricKind>(entry.DetailedKind, ignoreCase: true, out var detailedKind)
                ? detailedKind
                : null,
            StatusValue = entry.StatusValue,
            BooleanValue = entry.BooleanValue,
            IntegerValue = entry.IntegerValue,
            TimestampValue = entry.TimestampValue,
            AdditionalDisplayValues = (entry.AdditionalDisplayValues ?? new List<BalanceMetricAdditionalFileEntry>())
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .Select(a => new BalanceMetricAdditionalValue
                {
                    Name = a.Name,
                    Value = a.Value,
                    Unit = a.Unit,
                })
                .ToList(),
        };

    public static BalanceMetricFileEntry ToBalanceMetricFileEntry(BalanceMetric metric) =>
        new()
        {
            MetricId = metric.MetricId,
            DisplayName = metric.DisplayName,
            Unit = metric.Unit,
            Kind = metric.Kind.ToString(),
            AvailableAmount = metric.AvailableAmount,
            TotalAmount = metric.TotalAmount,
            UsedAmount = metric.UsedAmount,
            GrantedAmount = metric.GrantedAmount,
            ToppedUpAmount = metric.ToppedUpAmount,
            IsThresholdSupported = metric.IsThresholdSupported,
            IsUnlimited = metric.IsUnlimited,
            ValueKind = metric.ValueKind.ToString(),
            DetailedKind = metric.DetailedKind?.ToString(),
            StatusValue = metric.StatusValue,
            BooleanValue = metric.BooleanValue,
            IntegerValue = metric.IntegerValue,
            TimestampValue = metric.TimestampValue,
            AdditionalDisplayValues = metric.AdditionalDisplayValues
                .Select(a => new BalanceMetricAdditionalFileEntry
                {
                    Name = a.Name,
                    Value = a.Value,
                    Unit = a.Unit,
                })
                .ToList(),
        };

    /// <summary>v0.4.0 旧版货币余额 → 通用指标。</summary>
    public static BalanceMetricFileEntry ToMetricFileEntry(LegacyBalanceAmountFileEntry entry) =>
        ToBalanceMetricFileEntry(BalanceMetricIds.ToMetric(entry));
}
