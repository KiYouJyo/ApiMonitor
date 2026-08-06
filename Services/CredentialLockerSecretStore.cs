using Windows.Security.Credentials;
using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 基于 Windows Credential Locker（PasswordVault）的实现。
/// API Key 绝不写入 JSON、日志或测试快照；只使用 ApiMonitor 凭据资源。
/// </summary>
public sealed class CredentialLockerSecretStore : ISecretStore
{
    /// <summary>当前凭据资源名（ApiMonitor）。</summary>
    internal const string ResourceName = "ApiMonitor";

    /// <summary>非 primary 槽位的用户名后缀分隔符（账户 ID 为 GUID，不会冲突）。</summary>
    private const string SlotSuffixSeparator = "::";

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
            return _vault.Retrieve(ResourceName, UserName(accountId, CredentialSlots.Primary)) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Credential Locker 可用性探测：构造 PasswordVault 失败（系统策略/关闭）
    /// 即视为不可用；不读取、不写入任何凭据内容。
    /// </summary>
    public bool IsAvailable()
    {
        try
        {
            _ = new PasswordVault();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<string?> GetAsync(
        string accountId,
        CancellationToken cancellationToken,
        string slot = CredentialSlots.Primary)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var credential = _vault.Retrieve(ResourceName, UserName(accountId, slot));
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

    public Task SetAsync(
        string accountId,
        string secret,
        CancellationToken cancellationToken,
        string slot = CredentialSlots.Primary)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // 先移除同账户旧凭据再写入，保证可替换。
            try
            {
                var existing = _vault.Retrieve(ResourceName, UserName(accountId, slot));
                if (existing is not null)
                {
                    _vault.Remove(existing);
                }
            }
            catch
            {
                // 旧凭据不存在是正常情况。
            }

            _vault.Add(new PasswordCredential(ResourceName, UserName(accountId, slot), secret));
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
            foreach (string slot in CredentialSlots.All)
            {
                RemoveCredential(ResourceName, UserName(accountId, slot));
            }
        }
        catch
        {
            // 凭据不存在时删除视为成功。
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<string> GetPresentSlots(string accountId)
    {
        var present = new List<string>();
        foreach (string slot in CredentialSlots.All)
        {
            try
            {
                if (_vault.Retrieve(ResourceName, UserName(accountId, slot)) is not null)
                {
                    present.Add(slot);
                }
            }
            catch
            {
                // 单槽位读取失败按不存在处理。
            }
        }

        return present;
    }

    private void RemoveCredential(string resource, string accountId)
    {
        var credential = _vault.Retrieve(resource, accountId);
        if (credential is not null)
        {
            _vault.Remove(credential);
        }
    }

    /// <summary>
    /// primary 槽位继续使用“账户 ID”作为凭据用户名（旧数据保持可读）；
    /// 其他槽位使用“账户 ID::槽位名”，与旧凭据互不冲突。
    /// </summary>
    internal static string UserName(string accountId, string slot) =>
        string.Equals(slot, CredentialSlots.Primary, StringComparison.Ordinal)
            ? accountId
            : accountId + SlotSuffixSeparator + slot;
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
