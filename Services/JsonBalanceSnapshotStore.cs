using System.Text.Json;
using System.Text.Json.Serialization;
using ApiBalanceMonitor.Models;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 最新余额快照、查询时间与余额历史的 JSON 持久化实现。
/// v0.2.0 将 schemaVersion 升级到 2：v0.1.0 的最后成功快照
/// 迁移为该账户历史记录的第一条（不重复迁移）。
/// </summary>
public sealed class JsonBalanceSnapshotStore : IBalanceSnapshotStore, IBalanceHistoryStore
{
    public const string FileName = "balance-records.json";
    public const int CurrentSchemaVersion = 2;

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
        var result = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new BalanceRecordsFileData(),
            cancellationToken);

        var data = result.Data;

        if (data.SchemaVersion == 1)
        {
            // v0.1.0 → v0.2.0：为每个记录补历史列表，并把最后成功快照
            // 迁移为第一条历史记录（来源视为手动）。
            await BackupForMigrationAsync(cancellationToken);
            MigrateV1ToV2(data);
            data.SchemaVersion = CurrentSchemaVersion;
            await AtomicJsonFile.WriteAsync(_directory, FileName, data, _options, cancellationToken);
        }
        else if (data.SchemaVersion < 1 || data.SchemaVersion > CurrentSchemaVersion)
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

        var records = data.Records
            .Where(static r => !string.IsNullOrWhiteSpace(r.AccountId))
            .Select(StorageMapper.ToRecord)
            .ToList();

        return new BalanceRecordsLoadResult
        {
            Records = records,
            RecoveryMessage = result.RecoveryMessage,
        };
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

    private static void MigrateV1ToV2(BalanceRecordsFileData data)
    {
        foreach (var record in data.Records)
        {
            record.History ??= new List<HistoryFileEntry>();
            if (record.LastSuccessfulSnapshot is not { } snapshot || record.History.Count > 0)
            {
                continue;
            }

            record.History.Add(new HistoryFileEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                AccountId = record.AccountId,
                ProviderId = record.ProviderId,
                SucceededAtUtc = snapshot.RetrievedAt,
                Source = nameof(BalanceQuerySource.Manual),
                IsAvailable = snapshot.IsAvailable,
                Balances = snapshot.Balances
                    .Select(b => new BalanceAmountFileEntry
                    {
                        Currency = b.Currency,
                        TotalBalance = b.TotalBalance,
                        GrantedBalance = b.GrantedBalance,
                        ToppedUpBalance = b.ToppedUpBalance,
                    })
                    .ToList(),
            });
        }
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
