using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Providers;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 组合账户存储、凭据存储、快照存储与 Provider 注册表的门面实现。
/// </summary>
public sealed class AccountManager : IAccountManager
{
    private readonly IAccountStore _accountStore;
    private readonly IBalanceSnapshotStore _snapshotStore;
    private readonly ISecretStore _secretStore;
    private readonly ProviderRegistry _registry;
    private readonly AppLog _log;

    private readonly List<ApiAccount> _accounts = new();
    private readonly Dictionary<string, AccountBalanceRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public AccountManager(
        IAccountStore accountStore,
        IBalanceSnapshotStore snapshotStore,
        ISecretStore secretStore,
        ProviderRegistry registry,
        AppLog log)
    {
        _accountStore = accountStore;
        _snapshotStore = snapshotStore;
        _secretStore = secretStore;
        _registry = registry;
        _log = log;
    }

    public IReadOnlyList<string> RecoveryMessages { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<ProviderInfo> Providers =>
        _registry.All.Select(p => new ProviderInfo(p.ProviderId, p.DisplayName)).ToList();

    public async Task<IReadOnlyList<ApiAccount>> LoadAsync(CancellationToken cancellationToken)
    {
        var accounts = await _accountStore.LoadAsync(cancellationToken);
        var records = await _snapshotStore.LoadAsync(cancellationToken);

        var messages = new List<string>(2);
        if (accounts.RecoveryMessage is { } accountMessage)
        {
            messages.Add(accountMessage);
        }

        if (records.RecoveryMessage is { } recordMessage)
        {
            messages.Add(recordMessage);
        }

        RecoveryMessages = messages;

        _accounts.Clear();
        _accounts.AddRange(accounts.Accounts);

        _records.Clear();
        foreach (var record in records.Records)
        {
            _records[record.AccountId] = record;
        }

        _log.Info($"已加载 {_accounts.Count} 个账户。");
        return _accounts;
    }

    public Task<IReadOnlyList<ApiAccount>> GetAllAccountsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ApiAccount>>(_accounts.ToList());
    }

    public Task<AccountBalanceRecord?> GetRecordAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _records.TryGetValue(accountId, out var record) ? record : null);
    }

    public Task<string?> GetApiKeyAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _secretStore.GetAsync(accountId, cancellationToken);
    }

    public async Task<BalanceQueryResult> TestConnectionAsync(
        string providerId,
        string? apiKey,
        string? accountId,
        CancellationToken cancellationToken)
    {
        var provider = _registry.GetById(providerId);
        if (provider is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.NotSupported,
                "不支持的 Provider。");
        }

        string? key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        if (key is null && accountId is not null)
        {
            key = await _secretStore.GetAsync(accountId, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.MissingCredential,
                "请输入 API Key 后再测试连接。");
        }

        var probe = new ApiAccount
        {
            AccountId = accountId ?? "<test>",
            ProviderId = providerId,
            DisplayName = "<test>",
            HasCredential = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        return await provider.QueryBalanceAsync(probe, key, cancellationToken);
    }

    public async Task<ApiAccount> SaveAccountAsync(
        string? accountId,
        string providerId,
        string displayName,
        string? newApiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("账户显示名称不能为空。", nameof(displayName));
        }

        if (_registry.GetById(providerId) is null)
        {
            throw new ArgumentException($"不支持的 Provider: {providerId}", nameof(providerId));
        }

        string id = accountId ?? Guid.NewGuid().ToString("N");
        var existing = _accounts.FirstOrDefault(a =>
            string.Equals(a.AccountId, id, StringComparison.OrdinalIgnoreCase));

        var now = DateTimeOffset.UtcNow;
        bool hasCredential = existing?.HasCredential ?? false;

        if (!string.IsNullOrWhiteSpace(newApiKey))
        {
            await _secretStore.SetAsync(id, newApiKey.Trim(), cancellationToken);
            hasCredential = true;
        }

        var account = new ApiAccount
        {
            AccountId = id,
            ProviderId = providerId,
            DisplayName = displayName.Trim(),
            HasCredential = hasCredential,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
        };

        if (existing is null)
        {
            _accounts.Add(account);
            if (!_records.ContainsKey(id))
            {
                _records[id] = new AccountBalanceRecord
                {
                    AccountId = id,
                    ProviderId = providerId,
                };
            }
        }
        else
        {
            int index = _accounts.FindIndex(a =>
                string.Equals(a.AccountId, id, StringComparison.OrdinalIgnoreCase));
            _accounts[index] = account;
        }

        await PersistAsync(cancellationToken);
        _log.Info($"已保存账户 {account.AccountId}。");
        return account;
    }

    public async Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        await _secretStore.DeleteAsync(accountId, cancellationToken);

        _accounts.RemoveAll(a =>
            string.Equals(a.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        _records.Remove(accountId);

        await PersistAsync(cancellationToken);
        _log.Info($"已删除账户 {accountId} 及其凭据与余额快照。");
    }

    public async Task<BalanceQueryResult> RefreshAccountAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        var account = _accounts.FirstOrDefault(a =>
            string.Equals(a.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.AccountNotFound,
                "未找到该账户。");
        }

        var provider = _registry.GetById(account.ProviderId);
        if (provider is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.NotSupported,
                "该账户的 Provider 不受支持。");
        }

        _records.TryGetValue(accountId, out var record);
        record ??= new AccountBalanceRecord
        {
            AccountId = accountId,
            ProviderId = account.ProviderId,
        };

        if (!account.HasCredential)
        {
            record.LastQueryAttemptAt = DateTimeOffset.UtcNow;
            _records[accountId] = record;
            await PersistAsync(cancellationToken);
            return BalanceQueryResult.Failure(
                BalanceErrorKind.MissingCredential,
                "该账户没有保存的 API Key。");
        }

        var apiKey = await _secretStore.GetAsync(accountId, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            record.LastQueryAttemptAt = DateTimeOffset.UtcNow;
            _records[accountId] = record;
            await PersistAsync(cancellationToken);
            return BalanceQueryResult.Failure(
                BalanceErrorKind.MissingCredential,
                "读取保存的 API Key 失败。");
        }

        var result = await provider.QueryBalanceAsync(account, apiKey, cancellationToken);

        record.LastQueryAttemptAt = DateTimeOffset.UtcNow;
        if (result.IsSuccess && result.Snapshot is { } snapshot)
        {
            record.LastSuccessfulSnapshot = snapshot;
            record.LastQuerySuccessAt = record.LastQueryAttemptAt;
        }

        _records[accountId] = record;
        await PersistAsync(cancellationToken);
        return result;
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _accountStore.SaveAsync(_accounts, cancellationToken);
        await _snapshotStore.SaveAsync(_records.Values.ToList(), cancellationToken);
    }
}
