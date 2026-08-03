using ApiBalanceMonitor.Models;

namespace ApiBalanceMonitor.Services;

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
    public string Currency { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public decimal ThresholdAmount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
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
    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public DateTimeOffset RetrievedAt { get; set; }

    public List<BalanceAmountFileEntry> Balances { get; set; } = new();
}

public sealed class HistoryFileEntry
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public DateTimeOffset SucceededAtUtc { get; set; }

    public string Source { get; set; } = nameof(BalanceQuerySource.Manual);

    public bool IsAvailable { get; set; }

    public List<BalanceAmountFileEntry> Balances { get; set; } = new();
}

public sealed class BalanceAmountFileEntry
{
    public string Currency { get; set; } = string.Empty;

    public decimal TotalBalance { get; set; }

    public decimal GrantedBalance { get; set; }

    public decimal ToppedUpBalance { get; set; }
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
                    .Where(t => !string.IsNullOrWhiteSpace(t.Currency))
                    .Select(t => new BalanceThresholdRule
                    {
                        Currency = t.Currency,
                        IsEnabled = t.IsEnabled,
                        ThresholdAmount = t.ThresholdAmount,
                        CreatedAtUtc = t.CreatedAtUtc,
                        UpdatedAtUtc = t.UpdatedAtUtc,
                    })
                    .ToList(),
            }
            : new MonitoringSettings();

        return new ApiAccount
        {
            AccountId = entry.AccountId,
            ProviderId = entry.ProviderId,
            DisplayName = entry.DisplayName,
            HasCredential = entry.HasCredential,
            CreatedAtUtc = entry.CreatedAtUtc,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            Monitoring = monitoring,
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
            Monitoring = new MonitoringFileEntry
            {
                AutoRefreshEnabled = account.Monitoring.AutoRefreshEnabled,
                RefreshIntervalMinutes = account.Monitoring.RefreshIntervalMinutes,
                NextRefreshAtUtc = account.Monitoring.NextRefreshAtUtc,
                Thresholds = account.Monitoring.Thresholds
                    .Select(t => new ThresholdFileEntry
                    {
                        Currency = t.Currency,
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
            AccountId = entry.AccountId,
            ProviderId = entry.ProviderId,
            IsAvailable = entry.IsAvailable,
            RetrievedAt = entry.RetrievedAt,
            Balances = entry.Balances
                .Select(ToBalanceAmount)
                .ToList(),
        };

    public static SnapshotFileEntry ToSnapshotEntry(BalanceSnapshot snapshot) =>
        new()
        {
            AccountId = snapshot.AccountId,
            ProviderId = snapshot.ProviderId,
            IsAvailable = snapshot.IsAvailable,
            RetrievedAt = snapshot.RetrievedAt,
            Balances = snapshot.Balances
                .Select(ToBalanceAmountFileEntry)
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
            Balances = entry.Balances.Select(ToBalanceAmount).ToList(),
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
            Balances = entry.Balances.Select(ToBalanceAmountFileEntry).ToList(),
        };

    public static BalanceAmount ToBalanceAmount(BalanceAmountFileEntry entry) =>
        new()
        {
            Currency = entry.Currency,
            TotalBalance = entry.TotalBalance,
            GrantedBalance = entry.GrantedBalance,
            ToppedUpBalance = entry.ToppedUpBalance,
        };

    public static BalanceAmountFileEntry ToBalanceAmountFileEntry(BalanceAmount balance) =>
        new()
        {
            Currency = balance.Currency,
            TotalBalance = balance.TotalBalance,
            GrantedBalance = balance.GrantedBalance,
            ToppedUpBalance = balance.ToppedUpBalance,
        };
}
