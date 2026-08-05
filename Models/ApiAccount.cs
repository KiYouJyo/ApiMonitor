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

    /// <summary>
    /// 多字段凭据存在状态（slot → 是否存在；v0.9.0）。
    /// 只保存“存在”标志，绝不保存任何凭据值。
    /// 旧账户文件没有此字典时为空（表示只有传统 primary 凭据）。
    /// </summary>
    public IReadOnlyDictionary<string, bool> CredentialSlots { get; set; } =
        new Dictionary<string, bool>(StringComparer.Ordinal);
}

/// <summary>
/// 凭据槽位稳定 ID（v0.9.0）。每个槽位独立存入 Credential Locker；
/// 旧单密钥账户使用 <see cref="Primary"/>，且继续使用原 Resource+账户 ID 键。
/// </summary>
public static class CredentialSlots
{
    public const string Primary = "primary";
    public const string Secret = "secret";
    public const string Username = "username";
    public const string Password = "password";
    public const string BearerToken = "bearer-token";
    public const string QueryToken = "query-token";

    /// <summary>全部已知槽位（用于枚举 Credential Locker 中的存在状态）。</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { Primary, Secret, Username, Password, BearerToken, QueryToken };
}
