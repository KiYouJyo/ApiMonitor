using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 便携备份的 ZIP+JSON 实现（.apimonitor-backup）。
/// 只包含非敏感数据；导入为安全合并，失败回滚。
/// </summary>
public sealed class PortableBackupService : IPortableBackupService
{
    private readonly string _dataDirectory;
    private readonly IAccountStore _accountStore;
    private readonly IBalanceSnapshotStore _snapshotStore;
    private readonly INotificationSettingsStore _notificationSettingsStore;
    private readonly ITraySettingsStore _traySettingsStore;
    private readonly ICompactWindowSettingsStore _compactWindowSettingsStore;
    private readonly IAppearanceSettingsStore _appearanceSettingsStore;
    private readonly IReadOnlyList<string> _supportedProviderIds;
    private readonly JsonSerializerOptions _jsonOptions;

    public PortableBackupService(
        string dataDirectory,
        IAccountStore accountStore,
        IBalanceSnapshotStore snapshotStore,
        INotificationSettingsStore notificationSettingsStore,
        ITraySettingsStore traySettingsStore,
        ICompactWindowSettingsStore compactWindowSettingsStore,
        IAppearanceSettingsStore appearanceSettingsStore,
        IEnumerable<string> supportedProviderIds)
    {
        _dataDirectory = dataDirectory;
        _accountStore = accountStore;
        _snapshotStore = snapshotStore;
        _notificationSettingsStore = notificationSettingsStore;
        _traySettingsStore = traySettingsStore;
        _compactWindowSettingsStore = compactWindowSettingsStore;
        _appearanceSettingsStore = appearanceSettingsStore;
        _supportedProviderIds = supportedProviderIds.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    // ------------------------------------------------------------------
    // 导出
    // ------------------------------------------------------------------

    public async Task ExportAsync(string targetFilePath, CancellationToken cancellationToken)
    {
        string fullTarget = Path.GetFullPath(targetFilePath);
        string? targetDir = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var accounts = await _accountStore.LoadAsync(cancellationToken);
        var records = await _snapshotStore.LoadAsync(cancellationToken);
        var notification = await _notificationSettingsStore.LoadAsync(cancellationToken);
        var tray = await _traySettingsStore.LoadAsync(cancellationToken);
        var compact = await _compactWindowSettingsStore.LoadAsync(cancellationToken);
        var appearance = await _appearanceSettingsStore.LoadAsync(cancellationToken);

        // 打包内容：只含非敏感数据文件。
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [PortableBackupConstants.AccountsFileName] = Serialize(ToAccountsFileData(accounts.Accounts)),
            [PortableBackupConstants.BalanceRecordsFileName] = Serialize(ToBalanceRecordsFileData(records.Records)),
            [PortableBackupConstants.NotificationSettingsFileName] = Serialize(notification),
            [PortableBackupConstants.TraySettingsFileName] = Serialize(tray),
            [PortableBackupConstants.CompactWindowSettingsFileName] = Serialize(compact),
            [PortableBackupConstants.AppearanceSettingsFileName] = Serialize(appearance),
        };

        var manifest = new BackupManifest
        {
            BackupFormatVersion = PortableBackupConstants.BackupFormatVersion,
            DisplayVersion = AppInfo.DisplayVersion,
            PackageVersion = AppInfo.PackageVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ContainsSecrets = false,
            SupportedProviderIds = _supportedProviderIds.ToList(),
            Files = payloads
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new BackupFileManifestEntry
                {
                    Name = p.Key,
                    Size = p.Value.LongLength,
                    Sha256 = Sha256Hex(p.Value),
                })
                .ToList(),
        };
        payloads[PortableBackupConstants.ManifestFileName] = Serialize(manifest);

        // 写临时 ZIP 后原子替换，避免中途崩溃留下半成品。
        string tempPath = fullTarget + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var kv in payloads.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(kv.Key, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(kv.Value, cancellationToken);
                }
            }

            File.Move(tempPath, fullTarget, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // 清理失败不影响结果。
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // 校验与预览
    // ------------------------------------------------------------------

    public async Task<BackupImportPreview> InspectAsync(string sourceFilePath, CancellationToken cancellationToken)
    {
        using var extraction = await ExtractVerifiedAsync(sourceFilePath, cancellationToken);
        var manifest = ReadManifest(extraction.Directory);

        int accountCount = 0;
        int historyCount = 0;
        var providerIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var sections = new List<string>();

        var accountsFile = Path.Combine(extraction.Directory, PortableBackupConstants.AccountsFileName);
        if (File.Exists(accountsFile))
        {
            var accounts = Deserialize<AccountsFileData>(accountsFile);
            accountCount = accounts.Accounts.Count;
            providerIds.UnionWith(accounts.Accounts.Select(a => a.ProviderId).Where(p => !string.IsNullOrWhiteSpace(p)));
            sections.Add("accounts");
        }

        var recordsFile = Path.Combine(extraction.Directory, PortableBackupConstants.BalanceRecordsFileName);
        if (File.Exists(recordsFile))
        {
            var records = Deserialize<BalanceRecordsFileData>(recordsFile);
            historyCount = records.Records.Sum(r => r.History.Count);
            providerIds.UnionWith(records.Records.Select(r => r.ProviderId).Where(p => !string.IsNullOrWhiteSpace(p)));
            sections.Add("history");
        }

        foreach (var name in new[]
        {
            PortableBackupConstants.NotificationSettingsFileName,
            PortableBackupConstants.TraySettingsFileName,
            PortableBackupConstants.CompactWindowSettingsFileName,
            PortableBackupConstants.AppearanceSettingsFileName,
        })
        {
            if (File.Exists(Path.Combine(extraction.Directory, name)))
            {
                sections.Add(name);
            }
        }

        return new BackupImportPreview
        {
            Manifest = manifest,
            AccountCount = accountCount,
            HistoryEntryCount = historyCount,
            ProviderIds = providerIds.ToList(),
            SettingsSections = sections,
        };
    }

    public Task<bool> IsValidBackupAsync(string sourceFilePath, CancellationToken cancellationToken)
    {
        try
        {
            return InspectAsync(sourceFilePath, cancellationToken).ContinueWith(
                t => t.IsCompletedSuccessfully,
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    // ------------------------------------------------------------------
    // 导入（安全合并 + 回滚）
    // ------------------------------------------------------------------

    public async Task<BackupImportResult> ImportAsync(
        string sourceFilePath,
        BackupMergePreference preference,
        CancellationToken cancellationToken)
    {
        using var extraction = await ExtractVerifiedAsync(sourceFilePath, cancellationToken);
        _ = ReadManifest(extraction.Directory);

        // 导入开始前创建一份经过验证的本地安全快照。
        string snapshotDir = CreateSafetySnapshot(extraction.Directory);

        try
        {
            // 1. 账户合并。
            var localAccounts = await _accountStore.LoadAsync(cancellationToken);
            var incomingAccounts = Deserialize<AccountsFileData>(
                Path.Combine(extraction.Directory, PortableBackupConstants.AccountsFileName));
            var result = MergeAccounts(localAccounts.Accounts, incomingAccounts.Accounts, preference);

            // 2. 历史合并（按稳定 Id 去重）。
            var localRecords = await _snapshotStore.LoadAsync(cancellationToken);
            var incomingRecords = Deserialize<BalanceRecordsFileData>(
                Path.Combine(extraction.Directory, PortableBackupConstants.BalanceRecordsFileName));
            var historyResult = MergeRecords(localRecords.Records, incomingRecords.Records);

            // 3. 设置合并。
            if (File.Exists(Path.Combine(extraction.Directory, PortableBackupConstants.NotificationSettingsFileName)))
            {
                var incomingNotification = Deserialize<NotificationGlobalSettings>(
                    Path.Combine(extraction.Directory, PortableBackupConstants.NotificationSettingsFileName));
                var localNotification = await _notificationSettingsStore.LoadAsync(cancellationToken);
                await _notificationSettingsStore.SaveAsync(
                    MergeGlobalSettings(localNotification, incomingNotification, preference),
                    cancellationToken);
            }

            if (File.Exists(Path.Combine(extraction.Directory, PortableBackupConstants.TraySettingsFileName)))
            {
                var incomingTray = Deserialize<TraySettings>(
                    Path.Combine(extraction.Directory, PortableBackupConstants.TraySettingsFileName));
                var localTray = await _traySettingsStore.LoadAsync(cancellationToken);
                await _traySettingsStore.SaveAsync(MergeTray(localTray, incomingTray, preference), cancellationToken);
            }

            if (File.Exists(Path.Combine(extraction.Directory, PortableBackupConstants.CompactWindowSettingsFileName)))
            {
                var incomingCompact = Deserialize<CompactWindowSettings>(
                    Path.Combine(extraction.Directory, PortableBackupConstants.CompactWindowSettingsFileName));
                var localCompact = await _compactWindowSettingsStore.LoadAsync(cancellationToken);
                await _compactWindowSettingsStore.SaveAsync(
                    MergeCompact(localCompact, incomingCompact, preference),
                    cancellationToken);
            }

            if (File.Exists(Path.Combine(extraction.Directory, PortableBackupConstants.AppearanceSettingsFileName)))
            {
                var incomingAppearance = Deserialize<AppearanceSettingsData>(
                    Path.Combine(extraction.Directory, PortableBackupConstants.AppearanceSettingsFileName));
                var localAppearance = await _appearanceSettingsStore.LoadAsync(cancellationToken);
                await _appearanceSettingsStore.SaveAsync(
                    MergeAppearance(localAppearance, incomingAppearance, preference),
                    cancellationToken);
            }

            // 4. 写回账户与历史（在全部校验通过后一次性保存）。
            await _accountStore.SaveAsync(result.MergedAccounts, cancellationToken);
            await _snapshotStore.SaveAsync(historyResult.MergedRecords, cancellationToken);

            return new BackupImportResult
            {
                AddedAccounts = result.Added,
                UpdatedAccounts = result.Updated,
                SkippedAccounts = result.Skipped,
                FailedAccounts = result.Failed,
                AddedHistoryEntries = historyResult.Added,
                SkippedHistoryEntries = historyResult.Skipped,
                AccountsNeedingCredential = result.NeedingCredential,
            };
        }
        catch
        {
            // 导入失败：回滚本次变更（从快照恢复全部受影响文件）。
            RestoreSnapshot(snapshotDir);
            throw;
        }
        finally
        {
            TryDeleteDirectory(snapshotDir);
        }
    }

    // ------------------------------------------------------------------
    // 合并逻辑
    // ------------------------------------------------------------------

    private (IReadOnlyList<ApiAccount> MergedAccounts, int Added, int Updated, int Skipped, int Failed, IReadOnlyList<string> NeedingCredential)
        MergeAccounts(
            IReadOnlyList<ApiAccount> local,
            IReadOnlyList<AccountFileEntry> incoming,
            BackupMergePreference preference)
    {
        var merged = new List<ApiAccount>(local);
        var byId = merged.ToDictionary(a => a.AccountId, StringComparer.OrdinalIgnoreCase);
        int added = 0, updated = 0, skipped = 0, failed = 0;
        var needingCredential = new List<string>();

        foreach (var entry in incoming)
        {
            if (string.IsNullOrWhiteSpace(entry.AccountId))
            {
                failed++;
                continue;
            }

            if (!_supportedProviderIds.Any(p => string.Equals(p, entry.ProviderId, StringComparison.OrdinalIgnoreCase)))
            {
                // 本机不支持的 Provider：跳过，不导入。
                skipped++;
                continue;
            }

            if (byId.TryGetValue(entry.AccountId, out var existing))
            {
                // 已存在相同 AccountId：保留本机 Credential Locker 凭据。
                var updatedAccount = MergeExisting(existing, entry, preference);
                int index = merged.FindIndex(a => string.Equals(a.AccountId, entry.AccountId, StringComparison.OrdinalIgnoreCase));
                merged[index] = updatedAccount;
                byId[entry.AccountId] = updatedAccount;
                updated++;
            }
            else
            {
                // 新 AccountId：导入账户元数据，但标记“需要重新输入凭据”。
                var created = ToNewAccount(entry);
                merged.Add(created);
                byId[entry.AccountId] = created;
                added++;
                needingCredential.Add(entry.AccountId);
            }
        }

        return (merged, added, updated, skipped, failed, needingCredential);
    }

    private static ApiAccount MergeExisting(ApiAccount existing, AccountFileEntry incoming, BackupMergePreference preference)
    {
        var incomingAccount = StorageMapper.ToAccount(incoming);

        MonitoringSettings monitoring = preference == BackupMergePreference.PreferImport
            ? incomingAccount.Monitoring
            : existing.Monitoring;

        return new ApiAccount
        {
            AccountId = existing.AccountId,
            ProviderId = existing.ProviderId,
            DisplayName = preference == BackupMergePreference.PreferImport && !string.IsNullOrWhiteSpace(incomingAccount.DisplayName)
                ? incomingAccount.DisplayName
                : existing.DisplayName,
            // 凭据状态始终保留本机（导入不含凭据内容）。
            HasCredential = existing.HasCredential,
            CreatedAtUtc = existing.CreatedAtUtc,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Monitoring = monitoring,
            CredentialMode = preference == BackupMergePreference.PreferImport
                ? incomingAccount.CredentialMode
                : existing.CredentialMode,
            Notification = preference == BackupMergePreference.PreferImport
                ? incomingAccount.Notification
                : existing.Notification,
        };
    }

    private static ApiAccount ToNewAccount(AccountFileEntry entry)
    {
        var account = StorageMapper.ToAccount(entry);
        return new ApiAccount
        {
            AccountId = account.AccountId,
            ProviderId = account.ProviderId,
            DisplayName = account.DisplayName,
            // 新导入但无凭据：标记需要重新输入凭据。
            HasCredential = false,
            CreatedAtUtc = account.CreatedAtUtc,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Monitoring = new MonitoringSettings
            {
                // 无凭据的账户默认关闭自动刷新。
                AutoRefreshEnabled = false,
                RefreshIntervalMinutes = account.Monitoring.RefreshIntervalMinutes,
                NextRefreshAtUtc = null,
                Thresholds = account.Monitoring.Thresholds,
            },
            CredentialMode = account.CredentialMode,
            Notification = new AccountNotificationSettings
            {
                // 无凭据的账户默认关闭系统通知。
                NotificationsEnabled = false,
                RepeatIntervalHours = account.Notification.RepeatIntervalHours,
                RecoveryNotificationsEnabled = false,
            },
        };
    }

    private (IReadOnlyList<AccountBalanceRecord> MergedRecords, int Added, int Skipped)
        MergeRecords(
            IReadOnlyList<AccountBalanceRecord> local,
            IReadOnlyList<BalanceRecordFileEntry> incoming)
    {
        var merged = new List<AccountBalanceRecord>(local);
        var byId = merged.ToDictionary(r => r.AccountId, StringComparer.OrdinalIgnoreCase);
        int added = 0, skipped = 0;

        foreach (var entry in incoming)
        {
            if (string.IsNullOrWhiteSpace(entry.AccountId))
            {
                continue;
            }

            if (byId.TryGetValue(entry.AccountId, out var existing))
            {
                var knownIds = existing.History.Select(h => h.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var toAdd = entry.History
                    .Where(h => !knownIds.Contains(h.Id))
                    .Select(StorageMapper.ToHistoryEntry)
                    .ToList();
                added += toAdd.Count;
                skipped += entry.History.Count - toAdd.Count;

                if (toAdd.Count > 0)
                {
                    var mergedHistory = existing.History.Concat(toAdd)
                        .OrderByDescending(h => h.SucceededAtUtc)
                        .ThenBy(h => h.Id)
                        .ToList();
                    int index = merged.FindIndex(r => string.Equals(r.AccountId, entry.AccountId, StringComparison.OrdinalIgnoreCase));
                    merged[index] = new AccountBalanceRecord
                    {
                        AccountId = existing.AccountId,
                        ProviderId = existing.ProviderId,
                        LastQueryAttemptAt = existing.LastQueryAttemptAt,
                        LastQuerySuccessAt = existing.LastQuerySuccessAt,
                        LastSuccessfulSnapshot = existing.LastSuccessfulSnapshot,
                        History = mergedHistory,
                    };
                    byId[entry.AccountId] = merged[index];
                }
            }
            else
            {
                var record = StorageMapper.ToRecord(entry);
                merged.Add(record);
                byId[entry.AccountId] = record;
                added += record.History.Count;
            }
        }

        return (merged, added, skipped);
    }

    private static NotificationGlobalSettings MergeGlobalSettings(
        NotificationGlobalSettings local,
        NotificationGlobalSettings incoming,
        BackupMergePreference preference) =>
        preference == BackupMergePreference.PreferImport ? incoming : local;

    private static TraySettings MergeTray(TraySettings local, TraySettings incoming, BackupMergePreference preference) =>
        preference == BackupMergePreference.PreferImport ? incoming : local;

    private static CompactWindowSettings MergeCompact(
        CompactWindowSettings local,
        CompactWindowSettings incoming,
        BackupMergePreference preference) =>
        preference == BackupMergePreference.PreferImport ? incoming : local;

    private static AppearanceSettingsData MergeAppearance(
        AppearanceSettingsData local,
        AppearanceSettingsData incoming,
        BackupMergePreference preference) =>
        preference == BackupMergePreference.PreferImport ? incoming : local;

    // ------------------------------------------------------------------
    // 校验与解压
    // ------------------------------------------------------------------

    private sealed class VerifiedExtraction : IDisposable
    {
        public required string Directory { get; init; }

        public void Dispose() => TryDeleteDirectory(Directory);
    }

    private async Task<VerifiedExtraction> ExtractVerifiedAsync(
        string sourceFilePath,
        CancellationToken cancellationToken)
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"ApiMonitor-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        bool success = false;
        try
        {
            await using var stream = new FileStream(
                sourceFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            if (archive.Entries.Count > PortableBackupConstants.MaxEntryCount)
            {
                throw new InvalidDataException("备份条目数量超出上限。");
            }

            long totalBytes = 0;
            var extractedSizes = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(entry.Name) || entry.FullName.Contains('/') || entry.FullName.Contains('\\'))
                {
                    // 只允许扁平结构（manifest 与其文件都在根）。
                    if (entry.FullName != entry.Name)
                    {
                        throw new InvalidDataException($"备份包含子目录或路径条目：{entry.FullName}");
                    }
                }

                string safeName = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(safeName)
                    || safeName == ".."
                    || safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    throw new InvalidDataException($"备份包含非法文件名：{entry.FullName}");
                }

                if (entry.Length > PortableBackupConstants.MaxSingleFileBytes)
                {
                    throw new InvalidDataException($"备份单文件超出大小上限：{entry.FullName}");
                }

                totalBytes += entry.Length;
                if (totalBytes > PortableBackupConstants.MaxTotalBytes)
                {
                    throw new InvalidDataException("备份解压总大小超出上限。");
                }

                string dest = Path.Combine(tempRoot, safeName);
                await using (var entryStream = entry.Open())
                await using (var fileStream = new FileStream(
                    dest,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await entryStream.CopyToAsync(fileStream, cancellationToken);
                }

                extractedSizes[safeName] = new FileInfo(dest).Length;
            }

            // 必须存在 manifest。
            string manifestPath = Path.Combine(tempRoot, PortableBackupConstants.ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException("备份缺少 manifest.json。");
            }

            var manifest = ReadManifest(tempRoot);

            // 校验每个文件的大小与 SHA-256。
            foreach (var fileEntry in manifest.Files)
            {
                string name = Path.GetFileName(fileEntry.Name);
                string path = Path.Combine(tempRoot, name);
                if (!File.Exists(path))
                {
                    throw new InvalidDataException($"备份清单包含缺失文件：{fileEntry.Name}");
                }

                long actualSize = new FileInfo(path).Length;
                if (actualSize != fileEntry.Size)
                {
                    throw new InvalidDataException($"备份文件大小不匹配：{fileEntry.Name}");
                }

                string actualHash = Sha256Hex(path);
                if (!string.Equals(actualHash, fileEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"备份文件校验和不匹配：{fileEntry.Name}");
                }
            }

            // 校验 JSON 可解析且版本受支持。
            ValidatePayloads(tempRoot, manifest);

            success = true;
            return new VerifiedExtraction { Directory = tempRoot };
        }
        finally
        {
            if (!success)
            {
                TryDeleteDirectory(tempRoot);
            }
        }
    }

    private static void ValidatePayloads(string directory, BackupManifest manifest)
    {
        if (manifest.BackupFormatVersion > PortableBackupConstants.BackupFormatVersion)
        {
            // 不覆盖无法识别的较新 schema。
            throw new InvalidDataException($"备份格式版本 {manifest.BackupFormatVersion} 高于当前支持版本。");
        }

        if (manifest.ContainsSecrets)
        {
            throw new InvalidDataException("备份清单标记为包含机密，已拒绝导入。");
        }

        var knownNames = new HashSet<string>(StringComparer.Ordinal)
        {
            PortableBackupConstants.AccountsFileName,
            PortableBackupConstants.BalanceRecordsFileName,
            PortableBackupConstants.NotificationSettingsFileName,
            PortableBackupConstants.TraySettingsFileName,
            PortableBackupConstants.CompactWindowSettingsFileName,
            PortableBackupConstants.AppearanceSettingsFileName,
        };

        foreach (var fileEntry in manifest.Files)
        {
            string name = Path.GetFileName(fileEntry.Name);
            if (!knownNames.Contains(name))
            {
                throw new InvalidDataException($"备份清单包含未知文件：{fileEntry.Name}");
            }

            string path = Path.Combine(directory, name);
            if (name == PortableBackupConstants.AccountsFileName)
            {
                var accounts = Deserialize<AccountsFileData>(path);
                if (accounts.SchemaVersion > JsonAccountStore.CurrentSchemaVersion)
                {
                    throw new InvalidDataException("备份账户数据版本高于当前支持版本。");
                }
            }
            else if (name == PortableBackupConstants.BalanceRecordsFileName)
            {
                var records = Deserialize<BalanceRecordsFileData>(path);
                if (records.SchemaVersion > JsonBalanceSnapshotStore.CurrentSchemaVersion)
                {
                    throw new InvalidDataException("备份余额数据版本高于当前支持版本。");
                }
            }
            else
            {
                _ = Deserialize<JsonElement>(path); // 只验证 JSON 可解析。
            }
        }
    }

    private static BackupManifest ReadManifest(string directory)
    {
        string manifestPath = Path.Combine(directory, PortableBackupConstants.ManifestFileName);
        var manifest = Deserialize<BackupManifest>(manifestPath);
        if (manifest.BackupFormatVersion < 1 || manifest.ContainsSecrets)
        {
            throw new InvalidDataException("备份清单无效或不安全。");
        }

        return manifest;
    }

    private byte[] Serialize<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);

    private static T Deserialize<T>(string path)
    {
        try
        {
            string text = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<T>(
                text,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return result ?? throw new InvalidDataException("JSON 内容为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"JSON 解析失败（{Path.GetFileName(path)}）：{ex.Message}");
        }
    }

    private static AccountsFileData ToAccountsFileData(IReadOnlyList<ApiAccount> accounts) =>
        new()
        {
            SchemaVersion = JsonAccountStore.CurrentSchemaVersion,
            Accounts = accounts.Select(StorageMapper.ToEntry).ToList(),
        };

    private static BalanceRecordsFileData ToBalanceRecordsFileData(IReadOnlyList<AccountBalanceRecord> records) =>
        new()
        {
            SchemaVersion = JsonBalanceSnapshotStore.CurrentSchemaVersion,
            Records = records.Select(StorageMapper.ToEntry).ToList(),
        };

    private static string Sha256Hex(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    // ------------------------------------------------------------------
    // 本地安全快照与回滚
    // ------------------------------------------------------------------

    private string CreateSafetySnapshot(string extractionDirectory)
    {
        string snapshotDir = Path.Combine(
            Path.GetTempPath(),
            $"ApiMonitor-import-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(snapshotDir);

        string[] trackedFiles =
        {
            JsonAccountStore.FileName,
            JsonBalanceSnapshotStore.FileName,
            JsonNotificationSettingsStore.FileName,
            JsonTraySettingsStore.FileName,
            CompactWindowSettingsStore.FileName,
            JsonAppearanceSettingsStore.FileName,
        };

        foreach (var fileName in trackedFiles)
        {
            string src = Path.Combine(_dataDirectory, fileName);
            if (File.Exists(src))
            {
                File.Copy(src, Path.Combine(snapshotDir, fileName), overwrite: true);
            }
        }

        return snapshotDir;
    }

    private void RestoreSnapshot(string snapshotDir)
    {
        // 回滚：把快照文件复制回本地数据目录（调用方负责删除快照目录）。
        foreach (var file in Directory.EnumerateFiles(snapshotDir))
        {
            string name = Path.GetFileName(file);
            string dest = Path.Combine(_dataDirectory, name);
            try
            {
                File.Copy(file, dest, overwrite: true);
            }
            catch
            {
                // 单个文件恢复失败不掩盖原始异常。
            }
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // 清理失败不影响主流程。
        }
    }
}
