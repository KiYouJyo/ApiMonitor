namespace ApiMonitor.Models;

/// <summary>
/// 一个 API 账户的普通元数据。API Key 永不保存在此模型中，
/// 只通过 <see cref="Services.ISecretStore"/> 以账户 ID 关联保存。
/// </summary>
public sealed class ApiAccount
{
    public required string AccountId { get; init; }

    public required string ProviderId { get; init; }

    public required string DisplayName { get; init; }

    public bool HasCredential { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>自动刷新与阈值设置（v0.2.0 起持久化）。</summary>
    public MonitoringSettings Monitoring { get; set; } = new();

    /// <summary>Provider 专属的非敏感设置（如 OpenRouter 凭据模式）。API Key 永不在此保存。</summary>
    public string? CredentialMode { get; set; }

    /// <summary>
    /// Provider 专属的非敏感配置字段（如 xAI Team ID）。
    /// 与 CredentialMode 一样保存在 accounts.json / 备份中，绝不包含密钥。
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderConfig { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>每账户通知设置（v0.5.0；null 字段表示继承全局通知设置）。</summary>
    public AccountNotificationSettings Notification { get; set; } = new();
}
