using System.Text.Json;
using System.Text.Json.Serialization;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 账户普通数据与自动刷新/阈值设置的 JSON 持久化实现。
/// v0.2.0 将 schemaVersion 升级到 2：v0.1.0 文件（schemaVersion 1）
/// 迁移时补齐监控设置默认值，账户 ID / Provider ID / 凭据关联保持不变。
/// </summary>
public sealed class JsonAccountStore : IAccountStore
{
    public const string FileName = "accounts.json";
    public const int CurrentSchemaVersion = 2;

    private readonly string _directory;
    private readonly JsonSerializerOptions _options;

    public JsonAccountStore(string directory)
    {
        _directory = directory;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task<AccountsFileLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new AccountsFileData(),
            cancellationToken);

        var data = result.Data;

        if (data.SchemaVersion == 1)
        {
            // v0.1.0 → v0.2.0：补充监控设置默认值后原子写入 v2。
            await BackupForMigrationAsync(cancellationToken);
            foreach (var account in data.Accounts)
            {
                account.Monitoring ??= new MonitoringFileEntry();
            }

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
                ? "账户数据文件版本不受支持，已重置。"
                : $"账户数据文件版本不受支持，已备份为 {Path.GetFileName(backup)} 并重置。";

            return new AccountsFileLoadResult
            {
                Accounts = Array.Empty<ApiAccount>(),
                RecoveryMessage = message,
            };
        }

        var accounts = data.Accounts
            .Where(static a => !string.IsNullOrWhiteSpace(a.AccountId))
            .Select(StorageMapper.ToAccount)
            .ToList();

        return new AccountsFileLoadResult
        {
            Accounts = accounts,
            RecoveryMessage = result.RecoveryMessage,
        };
    }

    public Task SaveAsync(IReadOnlyList<ApiAccount> accounts, CancellationToken cancellationToken)
    {
        var data = new AccountsFileData
        {
            SchemaVersion = CurrentSchemaVersion,
            Accounts = accounts.Select(StorageMapper.ToEntry).ToList(),
        };

        return AtomicJsonFile.WriteAsync(_directory, FileName, data, _options, cancellationToken);
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
