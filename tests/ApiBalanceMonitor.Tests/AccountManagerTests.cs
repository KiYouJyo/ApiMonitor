using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Providers;
using ApiBalanceMonitor.Services;
using ApiBalanceMonitor.Tests.TestDoubles;
using ApiBalanceMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiBalanceMonitor.Tests;

public sealed class AccountManagerTests
{
    private static (AccountManager Manager, TempDirectory Temp, FakeSecretStore Secrets) CreateManager()
    {
        var temp = new TempDirectory();
        var secrets = new FakeSecretStore();
        var provider = new DeepSeekBalanceProvider(
            FakeHttpRequestService.Returning(
                """{ "is_available": true, "balance_infos": [ { "currency": "CNY", "total_balance": "9.90", "granted_balance": "0", "topped_up_balance": "9.90" } ] }"""));
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { provider });
        var manager = new AccountManager(
            new JsonAccountStore(temp.Path),
            new JsonBalanceSnapshotStore(temp.Path),
            secrets,
            registry,
            new AppLog(temp.Path));
        return (manager, temp, secrets);
    }

    [Fact]
    public async Task SaveAccount_StoresSecretInSecretStore_NotInAccountJson()
    {
        var (manager, temp, secrets) = CreateManager();

        var account = await manager.SaveAccountAsync(
            null,
            "deepseek",
            "我的 DeepSeek",
            "sk-real-looking-key-123",
            CancellationToken.None);

        Assert.True(account.HasCredential);
        Assert.Equal("sk-real-looking-key-123", secrets.Secrets[account.AccountId]);

        string json = await File.ReadAllTextAsync(Path.Combine(temp.Path, "accounts.json"));
        Assert.DoesNotContain("sk-real-looking-key-123", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAccount_PersistsSnapshotAndTimes()
    {
        var (manager, temp, secrets) = CreateManager();
        var account = await manager.SaveAccountAsync(
            "acct-refresh",
            "deepseek",
            "Test",
            "sk-key",
            CancellationToken.None);

        var result = await manager.RefreshAccountAsync(account.AccountId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var record = await manager.GetRecordAsync(account.AccountId, CancellationToken.None);
        Assert.NotNull(record);
        var loaded = record!;
        Assert.NotNull(loaded.LastQueryAttemptAt);
        Assert.NotNull(loaded.LastQuerySuccessAt);
        Assert.NotNull(loaded.LastSuccessfulSnapshot);
        Assert.Equal(9.90m, loaded.LastSuccessfulSnapshot!.Balances[0].TotalBalance);

        string json = await File.ReadAllTextAsync(Path.Combine(temp.Path, "balance-records.json"));
        Assert.Contains("lastSuccessfulSnapshot", json);
    }

    [Fact]
    public async Task DeleteAccount_RemovesSecretRecordAndAccount()
    {
        var (manager, temp, secrets) = CreateManager();
        var account = await manager.SaveAccountAsync(
            "acct-delete",
            "deepseek",
            "Test",
            "sk-key",
            CancellationToken.None);

        await manager.DeleteAccountAsync(account.AccountId, CancellationToken.None);

        Assert.False(secrets.Contains(account.AccountId));
        Assert.Empty(await manager.GetAllAccountsAsync(CancellationToken.None));
        Assert.Null(await manager.GetRecordAsync(account.AccountId, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshWithoutCredential_ReturnsMissingCredentialAndRecordsAttempt()
    {
        var (manager, _, _) = CreateManager();
        await manager.SaveAccountAsync(
            "acct-nokey",
            "deepseek",
            "Test",
            newApiKey: null,
            CancellationToken.None);

        var result = await manager.RefreshAccountAsync("acct-nokey", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.MissingCredential, result.Error!.Kind);
        var record = await manager.GetRecordAsync("acct-nokey", CancellationToken.None);
        Assert.NotNull(record);
        Assert.NotNull(record!.LastQueryAttemptAt);
    }

    [Fact]
    public async Task LoadAsync_CollectsRecoveryMessagesFromBothStores()
    {
        var (manager, temp, _) = CreateManager();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "accounts.json"), "broken{");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "balance-records.json"), "also broken{");

        await manager.LoadAsync(CancellationToken.None);

        Assert.Equal(2, manager.RecoveryMessages.Count);
        Assert.All(manager.RecoveryMessages, m => Assert.Contains("备份", m));
    }
}
