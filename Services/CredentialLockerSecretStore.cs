using Windows.Security.Credentials;

namespace ApiMonitor.Services;

/// <summary>
/// 基于 Windows Credential Locker（PasswordVault）的实现。
/// API Key 绝不写入 JSON、日志或测试快照。
/// 凭据资源名已由旧标识 ApiBalanceMonitor 迁移到 ApiMonitor：
/// 读取时优先新资源，找不到时兼容读取旧资源并安全迁移。
/// </summary>
public sealed class CredentialLockerSecretStore : ISecretStore
{
    /// <summary>当前凭据资源名（ApiMonitor）。</summary>
    internal const string ResourceName = "ApiMonitor";

    /// <summary>旧版凭据资源名（ApiBalanceMonitor），仅用于兼容读取与一次性迁移。</summary>
    internal const string LegacyResourceName = "ApiBalanceMonitor";

    private readonly IPasswordVaultAdapter _vault;
    private readonly AppLog? _log;

    public CredentialLockerSecretStore(AppLog? log = null)
        : this(new WindowsPasswordVaultAdapter(), log)
    {
    }

    internal CredentialLockerSecretStore(IPasswordVaultAdapter vault, AppLog? log = null)
    {
        _vault = vault;
        _log = log;
    }

    public bool Contains(string accountId)
    {
        try
        {
            return _vault.Retrieve(ResourceName, accountId) is not null
                || _vault.Retrieve(LegacyResourceName, accountId) is not null;
        }
        catch
        {
            return false;
        }
    }

    public Task<string?> GetAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var credential =
                _vault.Retrieve(ResourceName, accountId)
                ?? _vault.Retrieve(LegacyResourceName, accountId);
            if (credential is null)
            {
                return Task.FromResult<string?>(null);
            }

            if (credential.Resource == LegacyResourceName)
            {
                // 旧资源命中：先写入新资源，成功后删除旧条目（幂等）。
                TryMigrateLegacy(accountId, credential.Password);
            }

            return Task.FromResult<string?>(credential.Password);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(string accountId, string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // 先移除同账户旧凭据再写入，保证可替换。
            try
            {
                var existing = _vault.Retrieve(ResourceName, accountId);
                if (existing is not null)
                {
                    _vault.Remove(existing);
                }
            }
            catch
            {
                // 旧凭据不存在是正常情况。
            }

            _vault.Add(new PasswordCredential(ResourceName, accountId, secret));
            RemoveLegacy(accountId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _log?.Error($"保存凭据失败: {ex.GetType().Name}");
            throw;
        }
    }

    public Task DeleteAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            RemoveCredential(ResourceName, accountId);
            RemoveCredential(LegacyResourceName, accountId);
        }
        catch
        {
            // 凭据不存在时删除视为成功。
        }

        return Task.CompletedTask;
    }

    private void TryMigrateLegacy(string accountId, string secret)
    {
        try
        {
            _vault.Add(new PasswordCredential(ResourceName, accountId, secret));
            RemoveLegacy(accountId);
        }
        catch
        {
            // 迁移失败不影响本次读取；下次读取会再次尝试。
        }
    }

    private void RemoveLegacy(string accountId) =>
        RemoveCredential(LegacyResourceName, accountId);

    private void RemoveCredential(string resource, string accountId)
    {
        var credential = _vault.Retrieve(resource, accountId);
        if (credential is not null)
        {
            _vault.Remove(credential);
        }
    }
}

/// <summary>PasswordVault 的最小适配接口，便于对迁移逻辑做单元测试。</summary>
internal interface IPasswordVaultAdapter
{
    PasswordCredential? Retrieve(string resource, string userName);

    void Add(PasswordCredential credential);

    void Remove(PasswordCredential credential);
}

internal sealed class WindowsPasswordVaultAdapter : IPasswordVaultAdapter
{
    private readonly PasswordVault _vault = new();

    public PasswordCredential? Retrieve(string resource, string userName)
    {
        try
        {
            return _vault.Retrieve(resource, userName);
        }
        catch
        {
            return null;
        }
    }

    public void Add(PasswordCredential credential) => _vault.Add(credential);

    public void Remove(PasswordCredential credential) => _vault.Remove(credential);
}
