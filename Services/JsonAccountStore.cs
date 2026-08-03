using System.Text.Json;
using System.Text.Json.Serialization;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 账户普通数据与自动刷新/阈值设置的 JSON 持久化实现。
/// v0.2.0 将 schemaVersion 升级到 2（v0.1.0 文件补齐监控设置默认值）；
/// v0.5.0 将 schemaVersion 升级到 3：阈值规则从“币种”迁移为稳定
/// MetricId（DeepSeek 货币余额指标），并新增每账户通知设置与
/// Provider 非敏感选项。账户 ID / Provider ID / 凭据关联保持不变，
/// 迁移失败时备份旧文件，绝不要求用户重新输入 API Key。
/// </summary>
public sealed class JsonAccountStore : IAccountStore
{
    public const string FileName = "accounts.json";
    public const int CurrentSchemaVersion = 3;

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
        int? schema = await TryReadSchemaVersionAsync(cancellationToken);

        if (schema is null)
        {
            // 文件缺失或损坏：交给容错读取（损坏时备份并返回恢复提示）。
            var result = await AtomicJsonFile.ReadOrRecoverAsync(
                _directory,
                FileName,
                _options,
                static () => new AccountsFileData(),
                cancellationToken);
            return Map(result);
        }

        if (schema < CurrentSchemaVersion)
        {
            // v0.1.0（1）/ v0.2.0–v0.4.0（2）→ v0.5.0（3）：备份旧文件后
            // 一次性迁移（补齐监控默认值、币种阈值 → MetricId），幂等。
            var legacy = await AtomicJsonFile.ReadOrRecoverAsync(
                _directory,
                FileName,
                _options,
                static () => new LegacyAccountsFileData(),
                cancellationToken);

            await BackupForMigrationAsync(cancellationToken);
            var migrated = ToCurrent(legacy.Data);
            migrated.SchemaVersion = CurrentSchemaVersion;
            await AtomicJsonFile.WriteAsync(_directory, FileName, migrated, _options, cancellationToken);

            return new AccountsFileLoadResult
            {
                Accounts = migrated.Accounts
                    .Where(static a => !string.IsNullOrWhiteSpace(a.AccountId))
                    .Select(StorageMapper.ToAccount)
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
                ? L10n.Get("Store.AccountSchemaReset")
                : L10n.Format("Store.AccountSchemaResetBackedUp", Path.GetFileName(backup));

            return new AccountsFileLoadResult
            {
                Accounts = Array.Empty<ApiAccount>(),
                RecoveryMessage = message,
            };
        }

        var current = await AtomicJsonFile.ReadOrRecoverAsync(
            _directory,
            FileName,
            _options,
            static () => new AccountsFileData(),
            cancellationToken);
        return Map(current);
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

    private static AccountsFileLoadResult Map(AtomicJsonFile.LoadResult<AccountsFileData> result)
    {
        var accounts = result.Data.Accounts
            .Where(static a => !string.IsNullOrWhiteSpace(a.AccountId))
            .Select(StorageMapper.ToAccount)
            .ToList();

        return new AccountsFileLoadResult
        {
            Accounts = accounts,
            RecoveryMessage = result.RecoveryMessage,
        };
    }

    /// <summary>旧版账户文件 → 当前结构（补齐监控默认值 + 币种阈值迁移为 MetricId）。</summary>
    private static AccountsFileData ToCurrent(LegacyAccountsFileData legacy)
    {
        var data = new AccountsFileData
        {
            SchemaVersion = CurrentSchemaVersion,
            Accounts = new List<AccountFileEntry>(legacy.Accounts.Count),
        };

        foreach (var entry in legacy.Accounts)
        {
            var monitoring = entry.Monitoring ?? new LegacyMonitoringFileEntry();
            data.Accounts.Add(new AccountFileEntry
            {
                AccountId = entry.AccountId,
                ProviderId = entry.ProviderId,
                DisplayName = entry.DisplayName,
                HasCredential = entry.HasCredential,
                CreatedAtUtc = entry.CreatedAtUtc,
                UpdatedAtUtc = entry.UpdatedAtUtc,
                Monitoring = new MonitoringFileEntry
                {
                    AutoRefreshEnabled = monitoring.AutoRefreshEnabled,
                    RefreshIntervalMinutes = monitoring.RefreshIntervalMinutes,
                    NextRefreshAtUtc = monitoring.NextRefreshAtUtc,
                    Thresholds = monitoring.Thresholds
                        .Where(t => !string.IsNullOrWhiteSpace(t.Currency))
                        .Select(t =>
                        {
                            string currency = t.Currency.Trim();
                            return new ThresholdFileEntry
                            {
                                MetricId = BalanceMetricIds.DeepSeekCurrencyTotal(currency),
                                DisplayName = BalanceMetricIds.DeepSeekCurrencyDisplayName(currency),
                                Unit = currency,
                                IsEnabled = t.IsEnabled,
                                ThresholdAmount = t.ThresholdAmount,
                                CreatedAtUtc = t.CreatedAtUtc,
                                UpdatedAtUtc = t.UpdatedAtUtc,
                            };
                        })
                        .ToList(),
                },
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
