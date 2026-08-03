using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Providers;
using ApiBalanceMonitor.Services;
using ApiBalanceMonitor.Tests.TestDoubles;
using ApiBalanceMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiBalanceMonitor.Tests;

public sealed class AccountManagerTests
{
    private static (AccountManager Manager, TempDirectory Temp, FakeSecretStore Secrets, FakeTimeProvider Time) CreateManager()
    {
        var temp = new TempDirectory();
        var secrets = new FakeSecretStore();
        var time = new FakeTimeProvider();
        var provider = new DeepSeekBalanceProvider(
            FakeHttpRequestService.Returning(
                """{ "is_available": true, "balance_infos": [ { "currency": "CNY", "total_balance": "9.90", "granted_balance": "0", "topped_up_balance": "9.90" } ] }"""));
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { provider });
        var manager = new AccountManager(
            new JsonAccountStore(temp.Path),
            new JsonBalanceSnapshotStore(temp.Path),
            secrets,
            registry,
            new AppLog(temp.Path),
            time);
        return (manager, temp, secrets, time);
    }

    private static MonitoringSettings DefaultMonitoring() => new();

    [Fact]
    public async Task SaveAccount_StoresSecretInSecretStore_NotInAccountJson()
    {
        var (manager, temp, secrets, _) = CreateManager();

        var account = await manager.SaveAccountAsync(
            null,
            "deepseek",
            "我的 DeepSeek",
            "sk-real-looking-key-123",
            DefaultMonitoring(),
            CancellationToken.None);

        Assert.True(account.HasCredential);
        Assert.Equal("sk-real-looking-key-123", secrets.Secrets[account.AccountId]);

        string json = await File.ReadAllTextAsync(Path.Combine(temp.Path, "accounts.json"));
        Assert.DoesNotContain("sk-real-looking-key-123", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAccount_PersistsSnapshotTimesAndHistory()
    {
        var (manager, temp, _, _) = CreateManager();
        var account = await manager.SaveAccountAsync(
            "acct-refresh",
            "deepseek",
            "Test",
            "sk-key",
            DefaultMonitoring(),
            CancellationToken.None);

        var result = await manager.RefreshAccountAsync(
            account.AccountId,
            BalanceQuerySource.Manual,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var record = await manager.GetRecordAsync(account.AccountId, CancellationToken.None);
        Assert.NotNull(record);
        var loaded = record!;
        Assert.NotNull(loaded.LastQueryAttemptAt);
        Assert.NotNull(loaded.LastQuerySuccessAt);
        Assert.NotNull(loaded.LastSuccessfulSnapshot);
        Assert.Equal(9.90m, loaded.LastSuccessfulSnapshot!.Balances[0].TotalBalance);
        var history = Assert.Single(loaded.History);
        Assert.Equal(BalanceQuerySource.Manual, history.Source);
        Assert.Equal("CNY", history.Balances[0].Currency);

        string json = await File.ReadAllTextAsync(Path.Combine(temp.Path, "balance-records.json"));
        Assert.Contains("lastSuccessfulSnapshot", json);
        Assert.Contains("history", json);
    }

    [Fact]
    public async Task RefreshFailure_KeepsLastSuccessAndAddsNoHistory()
    {
        var http = FakeHttpRequestService.Mutable(
            """{ "is_available": true, "balance_infos": [ { "currency": "CNY", "total_balance": "9.90", "granted_balance": "0", "topped_up_balance": "9.90" } ] }""");
        var provider = new DeepSeekBalanceProvider(http);
        var (manager, _, _, _) = CreateManager(provider);
        var account = await manager.SaveAccountAsync(
            "acct-fail",
            "deepseek",
            "Test",
            "sk-key",
            DefaultMonitoring(),
            CancellationToken.None);

        // 第一次成功
        await manager.RefreshAccountAsync(account.AccountId, BalanceQuerySource.Manual, CancellationToken.None);
        var before = await manager.GetRecordAsync(account.AccountId, CancellationToken.None);
        Assert.NotNull(before!.LastSuccessfulSnapshot);
        Assert.Single(before.History);

        // 第二次失败（网络错误）
        http.SetHandler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException()));
        var failure = await manager.RefreshAccountAsync(
            account.AccountId,
            BalanceQuerySource.Automatic,
            CancellationToken.None);

        Assert.False(failure.IsSuccess);
        var after = await manager.GetRecordAsync(account.AccountId, CancellationToken.None);
        Assert.NotNull(after!.LastSuccessfulSnapshot);
        Assert.Single(after.History);
        Assert.NotNull(after.LastQueryAttemptAt);
    }

    [Fact]
    public async Task ConcurrentRefresh_ReturnsBusyWithoutDuplicateQuery()
    {
        var (manager, _, _, _) = CreateManager();
        var account = await manager.SaveAccountAsync(
            "acct-busy",
            "deepseek",
            "Test",
            "sk-key",
            DefaultMonitoring(),
            CancellationToken.None);

        var gate = new TaskCompletionSource();
        var provider = new DeepSeekBalanceProvider(FakeHttpRequestService.Gated(gate, """{ "is_available": true, "balance_infos": [] }"""));
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { provider });
        var busyManager = new AccountManager(
            new JsonAccountStore(Path.Combine(Path.GetTempPath(), $"abm-busy-{Guid.NewGuid():N}")),
            new JsonBalanceSnapshotStore(Path.Combine(Path.GetTempPath(), $"abm-busy-{Guid.NewGuid():N}")),
            new FakeSecretStore(),
            registry,
            new AppLog(Path.GetTempPath()),
            new FakeTimeProvider());
        await busyManager.SaveAccountAsync(
            "acct-busy",
            "deepseek",
            "Test",
            "sk-key",
            DefaultMonitoring(),
            CancellationToken.None);

        var first = busyManager.RefreshAccountAsync(account.AccountId, BalanceQuerySource.Automatic, CancellationToken.None);
        var second = await busyManager.RefreshAccountAsync(account.AccountId, BalanceQuerySource.Manual, CancellationToken.None);

        Assert.Equal(BalanceErrorKind.Busy, second.Error!.Kind);
        gate.SetResult();
        var firstResult = await first;
        Assert.True(firstResult.IsSuccess);
    }

    [Fact]
    public async Task RefreshRecomputesNextRefreshTime()
    {
        var (manager, _, _, time) = CreateManager();
        var account = await manager.SaveAccountAsync(
            "acct-next",
            "deepseek",
            "Test",
            "sk-key",
            DefaultMonitoring(),
            CancellationToken.None);

        time.AdvanceMinutes(40);
        await manager.RefreshAccountAsync(account.AccountId, BalanceQuerySource.Manual, CancellationToken.None);

        var refreshed = await manager.GetAccountAsync(account.AccountId, CancellationToken.None);
        Assert.NotNull(refreshed);
        Assert.True(refreshed!.Monitoring.AutoRefreshEnabled);
        Assert.Equal(time.UtcNow.AddMinutes(30), refreshed.Monitoring.NextRefreshAtUtc);

        var due = await manager.GetAutoRefreshDueAccountIdsAsync(
            time.UtcNow.AddMinutes(29),
            CancellationToken.None);
        Assert.DoesNotContain(account.AccountId, due);

        var dueLater = await manager.GetAutoRefreshDueAccountIdsAsync(
            time.UtcNow.AddMinutes(31),
            CancellationToken.None);
        Assert.Contains(account.AccountId, dueLater);
    }

    [Fact]
    public async Task IntervalChange_ReschedulesImmediately()
    {
        var (manager, _, _, time) = CreateManager();
        var account = await manager.SaveAccountAsync(
            "acct-interval",
            "deepseek",
            "Test",
            "sk-key",
            DefaultMonitoring(),
            CancellationToken.None);

        var monitoring = new MonitoringSettings
        {
            AutoRefreshEnabled = true,
            RefreshIntervalMinutes = 5,
        };
        var updated = await manager.SaveAccountAsync(
            account.AccountId,
            "deepseek",
            "Test",
            newApiKey: null,
            monitoring,
            CancellationToken.None);

        Assert.Equal(time.UtcNow.AddMinutes(5), updated.Monitoring.NextRefreshAtUtc);
    }

    [Fact]
    public async Task DeleteAccount_RemovesSecretRecordAndHistory()
    {
        var (manager, temp, secrets, _) = CreateManager();
        var account = await manager.SaveAccountAsync(
            "acct-delete",
            "deepseek",
            "Test",
            "sk-key",
            DefaultMonitoring(),
            CancellationToken.None);
        await manager.RefreshAccountAsync(account.AccountId, BalanceQuerySource.Manual, CancellationToken.None);
        Assert.Single(await manager.GetHistoryAsync(account.AccountId, CancellationToken.None));

        await manager.DeleteAccountAsync(account.AccountId, CancellationToken.None);

        Assert.False(secrets.Contains(account.AccountId));
        Assert.Empty(await manager.GetAllAccountsAsync(CancellationToken.None));
        Assert.Null(await manager.GetRecordAsync(account.AccountId, CancellationToken.None));
        Assert.Empty(await manager.GetHistoryAsync(account.AccountId, CancellationToken.None));
    }

    [Fact]
    public async Task OverdueAccount_AfterResume_RefreshesOnlyOnce()
    {
        var (manager, _, _, time) = CreateManager();
        var account = await manager.SaveAccountAsync(
            "acct-overdue",
            "deepseek",
            "Test",
            "sk-key",
            DefaultMonitoring(),
            CancellationToken.None);

        // 休眠 5 小时（远超 30 分钟间隔）
        time.AdvanceMinutes(300);
        var due = await manager.GetAutoRefreshDueAccountIdsAsync(time.UtcNow, CancellationToken.None);
        Assert.Contains(account.AccountId, due);

        await manager.RefreshAccountAsync(account.AccountId, BalanceQuerySource.Automatic, CancellationToken.None);

        var after = await manager.GetAutoRefreshDueAccountIdsAsync(time.UtcNow, CancellationToken.None);
        Assert.DoesNotContain(account.AccountId, after);
    }

    [Fact]
    public async Task DeletedAccount_IsNoLongerDue()
    {
        var (manager, _, _, time) = CreateManager();
        var account = await manager.SaveAccountAsync(
            "acct-del-due",
            "deepseek",
            "Test",
            "sk-key",
            DefaultMonitoring(),
            CancellationToken.None);
        time.AdvanceMinutes(31);

        await manager.DeleteAccountAsync(account.AccountId, CancellationToken.None);

        var due = await manager.GetAutoRefreshDueAccountIdsAsync(time.UtcNow, CancellationToken.None);
        Assert.DoesNotContain(account.AccountId, due);
    }

    [Fact]
    public async Task ClearHistory_KeepsLatestSnapshot()
    {
        var (manager, _, _, _) = CreateManager();
        var account = await manager.SaveAccountAsync(
            "acct-clear",
            "deepseek",
            "Test",
            "sk-key",
            DefaultMonitoring(),
            CancellationToken.None);
        await manager.RefreshAccountAsync(account.AccountId, BalanceQuerySource.Manual, CancellationToken.None);

        await manager.ClearHistoryAsync(account.AccountId, CancellationToken.None);

        Assert.Empty(await manager.GetHistoryAsync(account.AccountId, CancellationToken.None));
        var record = await manager.GetRecordAsync(account.AccountId, CancellationToken.None);
        Assert.NotNull(record!.LastSuccessfulSnapshot);
    }

    [Fact]
    public async Task RefreshWithoutCredential_ReturnsMissingCredentialAndRecordsAttempt()
    {
        var (manager, _, _, _) = CreateManager();
        await manager.SaveAccountAsync(
            "acct-nokey",
            "deepseek",
            "Test",
            newApiKey: null,
            DefaultMonitoring(),
            CancellationToken.None);

        var result = await manager.RefreshAccountAsync(
            "acct-nokey",
            BalanceQuerySource.Automatic,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.MissingCredential, result.Error!.Kind);
        var record = await manager.GetRecordAsync("acct-nokey", CancellationToken.None);
        Assert.NotNull(record);
        Assert.NotNull(record!.LastQueryAttemptAt);
    }

    [Fact]
    public async Task LoadAsync_CollectsRecoveryMessagesFromBothStores()
    {
        var (manager, temp, _, _) = CreateManager();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "accounts.json"), "broken{");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "balance-records.json"), "also broken{");

        await manager.LoadAsync(CancellationToken.None);

        Assert.Equal(2, manager.RecoveryMessages.Count);
        Assert.All(manager.RecoveryMessages, m => Assert.Contains("备份", m));
    }

    [Fact]
    public async Task GetApiKey_ReturnsStoredSecretOrNull()
    {
        var (manager, _, _, _) = CreateManager();
        await manager.SaveAccountAsync(
            "acct-key",
            "deepseek",
            "Test",
            "sk-test-only-not-real",
            DefaultMonitoring(),
            CancellationToken.None);

        Assert.Equal(
            "sk-test-only-not-real",
            await manager.GetApiKeyAsync("acct-key", CancellationToken.None));
        Assert.Null(await manager.GetApiKeyAsync("missing", CancellationToken.None));
    }

    private static (AccountManager Manager, TempDirectory Temp, FakeSecretStore Secrets, FakeTimeProvider Time) CreateManager(
        IApiBalanceProvider provider)
    {
        var temp = new TempDirectory();
        var secrets = new FakeSecretStore();
        var time = new FakeTimeProvider();
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { provider });
        var manager = new AccountManager(
            new JsonAccountStore(temp.Path),
            new JsonBalanceSnapshotStore(temp.Path),
            secrets,
            registry,
            new AppLog(temp.Path),
            time);
        return (manager, temp, secrets, time);
    }
}
