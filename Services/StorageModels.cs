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
}

public sealed class SnapshotFileEntry
{
    public string AccountId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public DateTimeOffset RetrievedAt { get; set; }

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
    public static ApiAccount ToAccount(AccountFileEntry entry) =>
        new()
        {
            AccountId = entry.AccountId,
            ProviderId = entry.ProviderId,
            DisplayName = entry.DisplayName,
            HasCredential = entry.HasCredential,
            CreatedAtUtc = entry.CreatedAtUtc,
            UpdatedAtUtc = entry.UpdatedAtUtc,
        };

    public static AccountFileEntry ToEntry(ApiAccount account) =>
        new()
        {
            AccountId = account.AccountId,
            ProviderId = account.ProviderId,
            DisplayName = account.DisplayName,
            HasCredential = account.HasCredential,
            CreatedAtUtc = account.CreatedAtUtc,
            UpdatedAtUtc = account.UpdatedAtUtc,
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
        };

    public static BalanceSnapshot ToSnapshot(SnapshotFileEntry entry) =>
        new()
        {
            AccountId = entry.AccountId,
            ProviderId = entry.ProviderId,
            IsAvailable = entry.IsAvailable,
            RetrievedAt = entry.RetrievedAt,
            Balances = entry.Balances
                .Select(b => new BalanceAmount
                {
                    Currency = b.Currency,
                    TotalBalance = b.TotalBalance,
                    GrantedBalance = b.GrantedBalance,
                    ToppedUpBalance = b.ToppedUpBalance,
                })
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
                .Select(b => new BalanceAmountFileEntry
                {
                    Currency = b.Currency,
                    TotalBalance = b.TotalBalance,
                    GrantedBalance = b.GrantedBalance,
                    ToppedUpBalance = b.ToppedUpBalance,
                })
                .ToList(),
        };
}
