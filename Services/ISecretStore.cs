using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 安全凭据存储抽象。第一阶段使用 Windows Credential Locker，
/// 未来可替换为其他实现。凭据以账户 ID 作为稳定关联标识。
/// v0.9.0：支持多槽位凭据（primary/secret/username/password/bearer-token/
/// query-token），每个槽位独立存储；旧单密钥调用保持不变（默认 primary）。
/// </summary>
public interface ISecretStore
{
    bool Contains(string accountId);

    Task<string?> GetAsync(
        string accountId,
        CancellationToken cancellationToken,
        string slot = CredentialSlots.Primary);

    Task SetAsync(
        string accountId,
        string secret,
        CancellationToken cancellationToken,
        string slot = CredentialSlots.Primary);

    Task DeleteAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>返回该账户在 Credential Locker 中实际存在的槽位列表。</summary>
    IReadOnlyList<string> GetPresentSlots(string accountId);
}
