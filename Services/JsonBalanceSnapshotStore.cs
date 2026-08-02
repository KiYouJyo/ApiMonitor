using System.Text.Json;
using System.Text.Json.Serialization;
using ApiBalanceMonitor.Models;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 最近余额快照与查询时间的 JSON 持久化实现。
/// 只保存领域模型映射后的数据，不保存 API 原始响应。
/// </summary>
public sealed class JsonBalanceSnapshotStore : IBalanceSnapshotStore
{
    public const string FileName = "balance-records.json";
    public const int CurrentSchemaVersion = 1;

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
        if (data.SchemaVersion != CurrentSchemaVersion)
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
}
