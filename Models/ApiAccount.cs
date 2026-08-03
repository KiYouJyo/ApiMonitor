namespace ApiBalanceMonitor.Models;

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
}
