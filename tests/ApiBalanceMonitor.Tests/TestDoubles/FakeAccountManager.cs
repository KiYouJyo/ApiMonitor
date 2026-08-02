using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Providers;
using ApiBalanceMonitor.Services;

namespace ApiBalanceMonitor.Tests.TestDoubles;

public sealed class FakeAccountManager : IAccountManager
{
    public List<ProviderInfo> ProviderList { get; } = new() { new ProviderInfo("deepseek", "DeepSeek") };

    public List<ApiAccount> Accounts { get; } = new();

    public Dictionary<string, AccountBalanceRecord> Records { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> RecoveryMessagesList { get; } = new();

    public BalanceQueryResult RefreshResult { get; set; } =
        BalanceQueryResult.Failure(BalanceErrorKind.Unknown, "未配置测试结果");

    public TaskCompletionSource? RefreshGate { get; set; }

    public TaskCompletionSource? LoadGate { get; set; }

    public int RefreshCalls { get; private set; }

    public int SaveCalls { get; private set; }

    public int DeleteCalls { get; private set; }

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

    public Task<AccountBalanceRecord?> GetRecordAsync(string accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Records.TryGetValue(accountId, out var record) ? record : null);
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
        };
        Accounts.RemoveAll(a => a.AccountId == id);
        Accounts.Add(account);
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

    public async Task<BalanceQueryResult> RefreshAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        RefreshCalls++;
        if (RefreshGate is not null)
        {
            await RefreshGate.Task.WaitAsync(cancellationToken);
        }

        return RefreshResult;
    }
}
