using Windows.Security.Credentials;

namespace ApiMonitor.Services;

/// <summary>
/// 基于 Windows Credential Locker（PasswordVault）的实现。
/// API Key 绝不写入 JSON、日志或测试快照；只使用 ApiMonitor 凭据资源。
/// </summary>
public sealed class CredentialLockerSecretStore : ISecretStore
{
    /// <summary>当前凭据资源名（ApiMonitor）。</summary>
    internal const string ResourceName = "ApiMonitor";

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
            return _vault.Retrieve(ResourceName, accountId) is not null;
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
            var credential = _vault.Retrieve(ResourceName, accountId);
            if (credential is null)
            {
                return Task.FromResult<string?>(null);
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
        }
        catch
        {
            // 凭据不存在时删除视为成功。
        }

        return Task.CompletedTask;
    }

    private void RemoveCredential(string resource, string accountId)
    {
        var credential = _vault.Retrieve(resource, accountId);
        if (credential is not null)
        {
            _vault.Remove(credential);
        }
    }
}

/// <summary>PasswordVault 的最小适配接口，便于对凭据读写逻辑做单元测试。</summary>
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
