using Windows.Security.Credentials;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 基于 Windows Credential Locker（PasswordVault）的实现。
/// API Key 绝不写入 JSON、日志或测试快照。
/// </summary>
public sealed class CredentialLockerSecretStore : ISecretStore
{
    private const string ResourceName = "ApiBalanceMonitor";

    private readonly PasswordVault _vault = new();
    private readonly AppLog? _log;

    public CredentialLockerSecretStore(AppLog? log = null)
    {
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
            return Task.FromResult<string?>(_vault.Retrieve(ResourceName, accountId).Password);
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
            var credential = _vault.Retrieve(ResourceName, accountId);
            if (credential is not null)
            {
                _vault.Remove(credential);
            }
        }
        catch
        {
            // 凭据不存在时删除视为成功。
        }

        return Task.CompletedTask;
    }
}
