using System.Text.Json;
using System.Text.Json.Serialization;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 最新余额快照、查询时间与余额历史的 JSON 持久化实现。
/// v0.2.0 将 schemaVersion 升级到 2（v0.1.0 的最后成功快照迁移为历史第一条）；
/// v0.5.0 将 schemaVersion 升级到 3：旧版“币种余额”（Currency/TotalBalance/
/// GrantedBalance/ToppedUpBalance）无损转换为通用 BalanceMetric 指标，
/// 并为每个快照补齐稳定 SnapshotId（通知去重用）。迁移幂等且先备份旧文件。
/// </summary>
public sealed class JsonBalanceSnapshotStore : IBalanceSnapshotStore, IBalanceHistoryStore
{
    public const string FileName = "balance-records.json";
    public const int CurrentSchemaVersion = 3;

    private readonly string _directory;
    private readonly JsonSerializerOptions _options;

    public JsonBalanceSnapshotStore(string directory)
    {
        _directory = directory;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task<BalanceRecordsLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        int? schema = await TryReadSchemaVersionAsync(cancellationToken);

        if (schema is null)
        {
            var result = await AtomicJsonFile.ReadOrRecoverAsync(
                _directory,
                FileName,
                _options,
                static () => new BalanceRecordsFileData(),
                cancellationToken);
            return Map(result);
        }

        if (schema < CurrentSchemaVersion)
        {
            var legacy = await AtomicJsonFile.ReadOrRecoverAsync(
                _directory,
                FileName,
                _options,
                static () => new LegacyBalanceRecordsFileData(),
                cancellationToken);

            await BackupForMigrationAsync(cancellationToken);

            if (schema == 1)
            {
                MigrateV1ToV2(legacy.Data);
            }

            var migrated = ToCurrent(legacy.Data);
            migrated.SchemaVersion = CurrentSchemaVersion;
            await AtomicJsonFile.WriteAsync(_directory, FileName, migrated, _options, cancellationToken);

            return new BalanceRecordsLoadResult
            {
                Records = migrated.Records
                    .Where(static r => !string.IsNullOrWhiteSpace(r.AccountId))
                    .Select(StorageMapper.ToRecord)
                    .ToList(),
                RecoveryMessage = legacy.RecoveryMessage,
            };
        }

        if (schema > CurrentSchemaVersion)
        {
            string backup = await AtomicJsonFile.BackupCorruptFileAsync(
                _directory,
                FileName,
                cancellationToken);

            string message = string.IsNullOrEmpty(backup)
                ? "余额记录数据文件版本不受支持，已重置。"
                : $"余额记录数据文件版本不受支持，已备份为 {Path.GetFileName(backup)} 并重置。";

            return new BalanceRecordsLoadResult
            {
                Records = Array.Empty<AccountBalanceRecord>(),
                RecoveryMessage = message,
            };
        }

        var current = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new BalanceRecordsFileData(),
            cancellationToken);
        return Map(current);
    }

    public Task SaveAsync(IReadOnlyList<AccountBalanceRecord> records, CancellationToken cancellationToken)
    {
        var data = new BalanceRecordsFileData
        {
            SchemaVersion = CurrentSchemaVersion,
            Records = records.Select(StorageMapper.ToEntry).ToList(),
        };

        return AtomicJsonFile.WriteAsync(_directory, FileName, data, _options, cancellationToken);
    }

    public async Task<IReadOnlyList<BalanceHistoryEntry>> GetHistoryAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(cancellationToken);
        var record = loaded.Records.FirstOrDefault(r =>
            string.Equals(r.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        return record?.History
            .OrderByDescending(h => h.SucceededAtUtc)
            .ThenBy(h => h.Id)
            .ToList() ?? new List<BalanceHistoryEntry>();
    }

    public async Task<int> PruneAsync(CancellationToken cancellationToken)
    {
        var result = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new BalanceRecordsFileData(),
            cancellationToken);

        var data = result.Data;
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        int removed = 0;
        bool changed = false;

        foreach (var record in data.Records)
        {
            var before = record.History.Count;
            record.History = HistoryRetention
                .Apply(
                    record.History.Select(h => StorageMapper.ToHistoryEntry(h)),
                    nowUtc)
                .Select(h => StorageMapper.ToHistoryFileEntry(h))
                .ToList();
            int after = record.History.Count;
            removed += Math.Max(0, before - after);
            changed |= before != after;
        }

        if (changed)
        {
            data.SchemaVersion = CurrentSchemaVersion;
            await AtomicJsonFile.WriteAsync(_directory, FileName, data, _options, cancellationToken);
        }

        return removed;
    }

    private async Task<int?> TryReadSchemaVersionAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(_directory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return doc.RootElement.TryGetProperty("schemaVersion", out var element)
                && element.TryGetInt32(out int value)
                ? value
                : 0;
        }
        catch (Exception ex) when (
            ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static BalanceRecordsLoadResult Map(AtomicJsonFile.LoadResult<BalanceRecordsFileData> result)
    {
        var records = result.Data.Records
            .Where(static r => !string.IsNullOrWhiteSpace(r.AccountId))
            .Select(StorageMapper.ToRecord)
            .ToList();

        return new BalanceRecordsLoadResult
        {
            Records = records,
            RecoveryMessage = result.RecoveryMessage,
        };
    }

    private static void MigrateV1ToV2(LegacyBalanceRecordsFileData data)
    {
        foreach (var record in data.Records)
        {
            record.History ??= new List<LegacyHistoryFileEntry>();
            if (record.LastSuccessfulSnapshot is not { } snapshot || record.History.Count > 0)
            {
                continue;
            }

            record.History.Add(new LegacyHistoryFileEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                AccountId = record.AccountId,
                ProviderId = record.ProviderId,
                SucceededAtUtc = snapshot.RetrievedAt,
                Source = nameof(BalanceQuerySource.Manual),
                IsAvailable = snapshot.IsAvailable,
                Balances = snapshot.Balances,
            });
        }
    }

    /// <summary>旧版余额记录 → 当前结构（币种余额 → 通用指标，补齐 SnapshotId）。</summary>
    private static BalanceRecordsFileData ToCurrent(LegacyBalanceRecordsFileData legacy)
    {
        var data = new BalanceRecordsFileData
        {
            SchemaVersion = CurrentSchemaVersion,
            Records = new List<BalanceRecordFileEntry>(legacy.Records.Count),
        };

        foreach (var record in legacy.Records)
        {
            data.Records.Add(new BalanceRecordFileEntry
            {
                AccountId = record.AccountId,
                ProviderId = record.ProviderId,
                LastQueryAttemptAt = record.LastQueryAttemptAt,
                LastQuerySuccessAt = record.LastQuerySuccessAt,
                LastSuccessfulSnapshot = record.LastSuccessfulSnapshot is { } snapshot
                    ? new SnapshotFileEntry
                    {
                        SnapshotId = Guid.NewGuid().ToString("N"),
                        AccountId = snapshot.AccountId,
                        ProviderId = snapshot.ProviderId,
                        IsAvailable = snapshot.IsAvailable,
                        RetrievedAt = snapshot.RetrievedAt,
                        Metrics = (snapshot.Balances ?? new List<LegacyBalanceAmountFileEntry>())
                            .Where(b => !string.IsNullOrWhiteSpace(b.Currency))
                            .Select(StorageMapper.ToMetricFileEntry)
                            .ToList(),
                    }
                    : null,
                History = (record.History ?? new List<LegacyHistoryFileEntry>())
                    .Where(h => !string.IsNullOrWhiteSpace(h.Id))
                    .Select(h => new HistoryFileEntry
                    {
                        Id = h.Id,
                        AccountId = h.AccountId,
                        ProviderId = h.ProviderId,
                        SucceededAtUtc = h.SucceededAtUtc,
                        Source = h.Source,
                        IsAvailable = h.IsAvailable,
                        Metrics = (h.Balances ?? new List<LegacyBalanceAmountFileEntry>())
                            .Where(b => !string.IsNullOrWhiteSpace(b.Currency))
                            .Select(StorageMapper.ToMetricFileEntry)
                            .ToList(),
                    })
                    .ToList(),
            });
        }

        return data;
    }

    private async Task BackupForMigrationAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(_directory, FileName);
        if (!File.Exists(path))
        {
            return;
        }

        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        string backupPath = Path.Combine(_directory, $"{FileName}.migrated-backup-{stamp}.json");
        await Task.Run(() => File.Copy(path, backupPath, overwrite: false), cancellationToken);
    }
}
