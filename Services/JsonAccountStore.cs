using System.Text.Json;
using System.Text.Json.Serialization;
using ApiBalanceMonitor.Models;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 账户普通数据的 JSON 持久化实现。
/// 只保存账户 ID、Provider ID、显示名称、凭据存在标记与时间戳，
/// 绝不保存 API Key。
/// </summary>
public sealed class JsonAccountStore : IAccountStore
{
    public const string FileName = "accounts.json";
    public const int CurrentSchemaVersion = 1;

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
        if (data.SchemaVersion != CurrentSchemaVersion)
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
}
