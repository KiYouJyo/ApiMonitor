using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Services;

namespace ApiMonitor.Tests.TestDoubles;

public sealed class FakeAccountManager : IAccountManager
{
    public event EventHandler<AccountRefreshStartedEventArgs>? RefreshStarted;

    public event EventHandler<AccountRefreshCompletedEventArgs>? RefreshCompleted;

    public event EventHandler? AccountsChanged;

    public event EventHandler<AccountDeletedEventArgs>? AccountDeleted;

    public void RaiseAccountDeleted(string accountId) =>
        AccountDeleted?.Invoke(this, new AccountDeletedEventArgs { AccountId = accountId });

    public List<ProviderInfo> ProviderList { get; } = new()
    {
        new ProviderInfo(
            "deepseek",
            "DeepSeek",
            "测试 Provider",
            SupportsAccountBalance: true,
            SupportsKeyQuota: false,
            SupportedMetricKinds: new[] { BalanceMetricKind.MonetaryBalance },
            CredentialOptions: new[]
            {
                new ProviderCredentialOption("api-key", "API Key", "普通密钥", IsDefault: true),
            },
            ApiKeyInputHint: "sk-…",
            HelpUrl: "https://example.test/",
            SupportsTestConnection: true),
    };

    public List<ApiAccount> Accounts { get; } = new();

    public Dictionary<string, AccountBalanceRecord> Records { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> RecoveryMessagesList { get; } = new();

    public BalanceQueryResult RefreshResult { get; set; } =
        BalanceQueryResult.Failure(BalanceErrorKind.Unknown, "未配置测试结果");

    /// <summary>SaveAccountAsync 抛出的异常（模拟保存失败）。</summary>
    public Exception? SaveException { get; set; }

    public int GetAllAccountsCalls { get; private set; }

    public string? ApiKeyResult { get; set; }

    public TaskCompletionSource? RefreshGate { get; set; }

    public TaskCompletionSource? LoadGate { get; set; }

    public TaskCompletionSource? RefreshAllGate { get; set; }

    public int RefreshCalls { get; private set; }

    public BalanceQuerySource? LastRefreshSource { get; private set; }

    public List<string> RefreshedAccountIds { get; } = new();

    public int SaveCalls { get; private set; }

    public int DeleteCalls { get; private set; }

    public int ClearHistoryCalls { get; private set; }

    public List<BalanceHistoryEntry> HistoryResult { get; set; } = new();

    public List<string> DueAccountIds { get; set; } = new();

    public IReadOnlyList<ProviderInfo> Providers => ProviderList;

    public IReadOnlyList<string> RecoveryMessages => RecoveryMessagesList;

    public bool HasActiveRefresh => ActiveRefreshCount > 0;

    public int ActiveRefreshCount { get; set; }

    public int RefreshAllCalls { get; private set; }

    public BalanceQuerySource? LastRefreshAllSource { get; private set; }

    public async Task RefreshAllAccountsAsync(BalanceQuerySource source, CancellationToken cancellationToken)
    {
        RefreshAllCalls++;
        LastRefreshAllSource = source;
        if (RefreshAllGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        foreach (var accountId in Accounts.Where(a => a.HasCredential).Select(a => a.AccountId).ToList())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await RefreshAccountAsync(accountId, source, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ApiAccount>> LoadAsync(CancellationToken cancellationToken)
    {
        if (LoadGate is not null)
        {
            await LoadGate.Task.WaitAsync(cancellationToken);
        }

        return Accounts.ToList();
    }

    public Task<IReadOnlyList<ApiAccount>> GetAllAccountsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetAllAccountsCalls++;
        return Task.FromResult<IReadOnlyList<ApiAccount>>(Accounts.ToList());
    }

    public Task<ApiAccount?> GetAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Accounts.FirstOrDefault(a => a.AccountId == accountId));
    }

    public Task<AccountBalanceRecord?> GetRecordAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Records.TryGetValue(accountId, out var record) ? record : null);
    }

    public Task<string?> GetApiKeyAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApiKeyResult);
    }

    public Task<BalanceQueryResult> TestConnectionAsync(
        string providerId,
        string? credentialMode,
        string? apiKey,
        string? accountId,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? providerConfig = null,
        IReadOnlyDictionary<string, string>? credentialSlots = null) =>
        Task.FromResult(RefreshResult);

    public Task<ApiAccount> SaveAccountAsync(
        string? accountId,
        string providerId,
        string displayName,
        string? newApiKey,
        string? credentialMode,
        MonitoringSettings monitoring,
        CancellationToken cancellationToken,
        AccountNotificationSettings? notification = null,
        IReadOnlyDictionary<string, string>? providerConfig = null,
        IReadOnlyDictionary<string, string>? credentialSlots = null)
    {
        if (SaveException is not null)
        {
            throw SaveException;
        }

        cancellationToken.ThrowIfCancellationRequested();
        SaveCalls++;
        string id = accountId ?? $"acct-{SaveCalls}";
        var account = new ApiAccount
        {
            AccountId = id,
            ProviderId = providerId,
            DisplayName = displayName,
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CredentialMode = credentialMode,
            ProviderConfig = providerConfig is { Count: > 0 }
                ? new Dictionary<string, string>(providerConfig, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal),
            Monitoring = monitoring,
        };
        Accounts.RemoveAll(a => a.AccountId == id);
        Accounts.Add(account);
        if (!Records.ContainsKey(id))
        {
            Records[id] = new AccountBalanceRecord { AccountId = id, ProviderId = providerId };
        }

        AccountsChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(account);
    }

    public Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteCalls++;
        Accounts.RemoveAll(a => a.AccountId == accountId);
        Records.Remove(accountId);
        AccountsChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task<BalanceQueryResult> RefreshAccountAsync(
        string accountId,
        BalanceQuerySource source,
        CancellationToken cancellationToken)
    {
        RefreshCalls++;
        LastRefreshSource = source;
        RefreshedAccountIds.Add(accountId);
        if (RefreshGate is not null)
        {
            await RefreshGate.Task.WaitAsync(cancellationToken);
        }

        return RefreshResult;
    }

    public Task<IReadOnlyList<string>> GetAutoRefreshDueAccountIdsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(DueAccountIds.ToList());
    }

    public Task<IReadOnlyList<BalanceHistoryEntry>> GetHistoryAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BalanceHistoryEntry>>(HistoryResult.ToList());
    }

    public Task ClearHistoryAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClearHistoryCalls++;
        HistoryResult.Clear();
        return Task.CompletedTask;
    }

    public void RaiseRefreshStarted(string accountId, BalanceQuerySource source) =>
        RefreshStarted?.Invoke(this, new AccountRefreshStartedEventArgs
        {
            AccountId = accountId,
            Source = source,
        });

    public void RaiseRefreshCompleted(string accountId, BalanceQueryResult result, BalanceQuerySource source) =>
        RefreshCompleted?.Invoke(this, new AccountRefreshCompletedEventArgs
        {
            AccountId = accountId,
            Result = result,
            Source = source,
        });

    public void RaiseAccountsChanged() =>
        AccountsChanged?.Invoke(this, EventArgs.Empty);
}
