using ApiMonitor.Providers;

namespace ApiMonitor.Models;

/// <summary>
/// 打开账户编辑对话框所需的上下文。Provider 列表来自注册表，
/// UI 与模型不写死为只有 DeepSeek。
/// </summary>
public sealed class AccountEditorContext
{
    public string? AccountId { get; init; }

    public required IReadOnlyList<ProviderInfo> Providers { get; init; }

    public required string InitialProviderId { get; init; }

    public string? InitialDisplayName { get; init; }

    public bool HasStoredCredential { get; init; }

    /// <summary>编辑时当前账户保存的凭据模式（新增时为 null）。</summary>
    public string? CredentialMode { get; init; }

    /// <summary>编辑时当前账户保存的非敏感 Provider 配置（如 xAI Team ID）。</summary>
    public IReadOnlyDictionary<string, string> ProviderConfig { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>当前监控设置（编辑时带入，新增时为默认值）。</summary>
    public required MonitoringSettings InitialMonitoring { get; init; }

    /// <summary>当前每账户通知设置（编辑时带入，新增时为默认值）。</summary>
    public AccountNotificationSettings InitialNotification { get; init; } = new();

    /// <summary>当前快照的各指标余额，用于阈值设置与状态展示。</summary>
    public required IReadOnlyList<BalanceMetric> CurrentMetrics { get; init; }
}

/// <summary>
/// 用户点击“保存”后由对话框返回的结果。ApiKey 为 null 表示沿用已有凭据。
/// </summary>
public sealed class AccountEditorResult
{
    public bool SaveRequested { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    /// <summary>用户选择的 Provider 凭据模式（如 openrouter 的 api-key / management-key）。</summary>
    public string? CredentialMode { get; set; }

    /// <summary>用户填写的非敏感 Provider 配置字段（如 xAI Team ID）。</summary>
    public IReadOnlyDictionary<string, string> ProviderConfig { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public MonitoringSettings Monitoring { get; set; } = new();

    /// <summary>每账户通知设置（null 字段表示继承全局）。</summary>
    public AccountNotificationSettings Notification { get; set; } = new();
}
