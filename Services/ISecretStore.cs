namespace ApiBalanceMonitor.Services;

/// <summary>
/// 安全凭据存储抽象。第一阶段使用 Windows Credential Locker，
/// 未来可替换为其他实现。凭据以账户 ID 作为稳定关联标识。
/// </summary>
public interface ISecretStore
{
    bool Contains(string accountId);

    Task<string?> GetAsync(string accountId, CancellationToken cancellationToken);

    Task SetAsync(string accountId, string secret, CancellationToken cancellationToken);

    Task DeleteAsync(string accountId, CancellationToken cancellationToken);
}
