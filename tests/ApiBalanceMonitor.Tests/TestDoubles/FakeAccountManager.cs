using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Providers;
using ApiBalanceMonitor.Services;

namespace ApiBalanceMonitor.Tests.TestDoubles;

public sealed class FakeAccountManager : IAccountManager
{
    public event EventHandler<AccountRefreshStartedEventArgs>? RefreshStarted;

    public event EventHandler<AccountRefreshCompletedEventArgs>? RefreshCompleted;

    public List<ProviderInfo> ProviderList { get; } = new() { new ProviderInfo("deepseek", "DeepSeek") };

    public List<ApiAccount> Accounts { get; } = new();

    public Dictionary<string, AccountBalanceRecord> Records { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> RecoveryMessagesList { get; } = new();

    public BalanceQueryResult RefreshResult { get; set; } =
        BalanceQueryResult.Failure(BalanceErrorKind.Unknown, "未配置测试结果");

    public string? ApiKeyResult { get; set; }

    public TaskCompletionSource? RefreshGate { get; set; }

    public TaskCompletionSource? LoadGate { get; set; }

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
        string? apiKey,
        string? accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult(RefreshResult);

    public Task<ApiAccount> SaveAccountAsync(
        string? accountId,
        string providerId,
        string displayName,
        string? newApiKey,
        MonitoringSettings monitoring,
        CancellationToken cancellationToken)
    {
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
            Monitoring = monitoring,
        };
        Accounts.RemoveAll(a => a.AccountId == id);
        Accounts.Add(account);
        if (!Records.ContainsKey(id))
        {
            Records[id] = new AccountBalanceRecord { AccountId = id, ProviderId = providerId };
        }

        return Task.FromResult(account);
    }

    public Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteCalls++;
        Accounts.RemoveAll(a => a.AccountId == accountId);
        Records.Remove(accountId);
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
}
