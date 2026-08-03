namespace ApiMonitor.Services;

/// <summary>备份格式版本（v0.6.0 首发为 1）。</summary>
public static class PortableBackupConstants
{
    public const int BackupFormatVersion = 1;

    public const string ManifestFileName = "manifest.json";
    public const string AccountsFileName = "accounts.json";
    public const string BalanceRecordsFileName = "balance-records.json";
    public const string NotificationSettingsFileName = "notification-settings.json";
    public const string TraySettingsFileName = "tray-settings.json";
    public const string CompactWindowSettingsFileName = "compact-window-settings.json";
    public const string AppearanceSettingsFileName = "appearance-settings.json";

    /// <summary>扩展名（不含点）。</summary>
    public const string Extension = "apimonitor-backup";

    /// <summary>单文件解压大小上限（50 MB）。</summary>
    public const long MaxSingleFileBytes = 50 * 1024 * 1024;

    /// <summary>总解压大小上限（200 MB）。</summary>
    public const long MaxTotalBytes = 200 * 1024 * 1024;

    /// <summary>ZIP 内条目数量上限（防止 zip bomb 条目洪泛）。</summary>
    public const int MaxEntryCount = 10_000;
}

/// <summary>备份清单中的文件条目。</summary>
public sealed class BackupFileManifestEntry
{
    public string Name { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>备份清单（manifest.json）。</summary>
public sealed class BackupManifest
{
    public int BackupFormatVersion { get; set; } = PortableBackupConstants.BackupFormatVersion;

    /// <summary>ApiMonitor 用户可见版本（如 0.6.0）。</summary>
    public string DisplayVersion { get; set; } = string.Empty;

    /// <summary>MSIX PackageVersion（如 0.6.0.0）。</summary>
    public string PackageVersion { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>明确标记不包含机密。</summary>
    public bool ContainsSecrets { get; set; }

    /// <summary>备份创建时支持的 Provider ID 列表。</summary>
    public List<string> SupportedProviderIds { get; set; } = new();

    public List<BackupFileManifestEntry> Files { get; set; } = new();
}

/// <summary>导入预览（不修改任何状态）。</summary>
public sealed class BackupImportPreview
{
    public required BackupManifest Manifest { get; init; }

    /// <summary>备份内的账户数量。</summary>
    public int AccountCount { get; init; }

    /// <summary>备份内的历史条目数量。</summary>
    public int HistoryEntryCount { get; init; }

    /// <summary>备份内涉及的 Provider ID 列表。</summary>
    public IReadOnlyList<string> ProviderIds { get; init; } = Array.Empty<string>();

    /// <summary>备份包含的设置范围（有则列出）。</summary>
    public IReadOnlyList<string> SettingsSections { get; init; } = Array.Empty<string>();
}

/// <summary>阈值与设置合并策略（导入预览时用户选择）。</summary>
public enum BackupMergePreference
{
    /// <summary>保留本机已有账户的阈值与设置；仅新增新账户。</summary>
    KeepLocal,

    /// <summary>导入的阈值与设置优先（仅对已存在账户生效；凭据始终保留本机）。</summary>
    PreferImport,
}

/// <summary>导入结果统计。</summary>
public sealed class BackupImportResult
{
    public int AddedAccounts { get; init; }

    public int UpdatedAccounts { get; init; }

    public int SkippedAccounts { get; init; }

    public int FailedAccounts { get; init; }

    public int AddedHistoryEntries { get; init; }

    public int SkippedHistoryEntries { get; init; }

    /// <summary>需要重新输入 API Key 的账户 ID（新导入且无凭据）。</summary>
    public IReadOnlyList<string> AccountsNeedingCredential { get; init; } = Array.Empty<string>();
}

/// <summary>
/// v0.6.0：便携备份（导出/导入/校验）。
/// 备份为 ZIP + JSON（.apimonitor-backup），只包含非敏感数据；
/// API Key、Management Key、Credential Locker 内容、Authorization、
/// 日志、PFX、证书私钥、临时缓存、活动通知 Tag 与短期去重状态绝不导出。
/// 导入为安全合并：验证清单/哈希/JSON/版本，预览后合并，
/// 已有账户保留本机凭据，新账户标记“需要重新输入凭据”，失败回滚。
/// </summary>
public interface IPortableBackupService
{
    /// <summary>导出便携备份到指定文件路径。</summary>
    Task ExportAsync(string targetFilePath, CancellationToken cancellationToken);

    /// <summary>只验证备份文件并返回预览（不修改任何状态）。</summary>
    Task<BackupImportPreview> InspectAsync(string sourceFilePath, CancellationToken cancellationToken);

    /// <summary>验证并安全合并导入，失败时回滚本次变更。</summary>
    Task<BackupImportResult> ImportAsync(
        string sourceFilePath,
        BackupMergePreference preference,
        CancellationToken cancellationToken);

    /// <summary>备份文件是否可用（验证清单与哈希）。</summary>
    Task<bool> IsValidBackupAsync(string sourceFilePath, CancellationToken cancellationToken);
}
