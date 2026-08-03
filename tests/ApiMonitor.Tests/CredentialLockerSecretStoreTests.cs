using ApiMonitor.Services;
using Windows.Security.Credentials;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 凭据迁移测试：仅使用内存假凭据存储，绝不访问真实 Credential Locker。
/// </summary>
public sealed class CredentialLockerSecretStoreTests
{
    private sealed class FakeVault : IPasswordVaultAdapter
    {
        public Dictionary<(string Resource, string User), string> Items { get; } = new();

        public PasswordCredential? Retrieve(string resource, string userName)
        {
            return Items.TryGetValue((resource, userName), out var password)
                ? new PasswordCredential(resource, userName, password)
                : null;
        }

        public void Add(PasswordCredential credential) =>
            Items[(credential.Resource, credential.UserName)] = credential.Password;

        public void Remove(PasswordCredential credential) =>
            Items.Remove((credential.Resource, credential.UserName));
    }

    private static (CredentialLockerSecretStore Store, FakeVault Vault) Create()
    {
        var vault = new FakeVault();
        return (new CredentialLockerSecretStore(vault), vault);
    }

    [Fact]
    public async Task GetAsync_PrefersNewResource_AndKeepsLegacyUntouched()
    {
        var (store, vault) = Create();
        vault.Items[("ApiMonitor", "acct-1")] = "sk-test-only-not-real-new";
        vault.Items[("ApiBalanceMonitor", "acct-1")] = "sk-test-only-not-real-legacy";

        var secret = await store.GetAsync("acct-1", CancellationToken.None);

        Assert.Equal("sk-test-only-not-real-new", secret);
        Assert.True(vault.Items.ContainsKey(("ApiBalanceMonitor", "acct-1")));
    }

    [Fact]
    public async Task GetAsync_LegacyOnly_MigratesToNew_AndRemovesLegacy()
    {
        var (store, vault) = Create();
        vault.Items[("ApiBalanceMonitor", "acct-1")] = "sk-test-only-not-real-legacy";

        var secret = await store.GetAsync("acct-1", CancellationToken.None);

        Assert.Equal("sk-test-only-not-real-legacy", secret);
        Assert.Equal("sk-test-only-not-real-legacy", vault.Items[("ApiMonitor", "acct-1")]);
        Assert.False(vault.Items.ContainsKey(("ApiBalanceMonitor", "acct-1")));
    }

    [Fact]
    public async Task GetAsync_NoCredential_ReturnsNull()
    {
        var (store, _) = Create();

        var secret = await store.GetAsync("acct-missing", CancellationToken.None);

        Assert.Null(secret);
    }

    [Fact]
    public async Task GetAsync_MigrationIsIdempotent()
    {
        var (store, vault) = Create();
        vault.Items[("ApiBalanceMonitor", "acct-1")] = "sk-test-only-not-real-legacy";

        await store.GetAsync("acct-1", CancellationToken.None);
        var second = await store.GetAsync("acct-1", CancellationToken.None);

        Assert.Equal("sk-test-only-not-real-legacy", second);
        Assert.False(vault.Items.ContainsKey(("ApiBalanceMonitor", "acct-1")));
    }

    [Fact]
    public async Task SetAsync_WritesNew_AndRemovesLegacy()
    {
        var (store, vault) = Create();
        vault.Items[("ApiBalanceMonitor", "acct-1")] = "sk-test-only-not-real-old";

        await store.SetAsync("acct-1", "sk-test-only-not-real-new", CancellationToken.None);

        Assert.Equal("sk-test-only-not-real-new", vault.Items[("ApiMonitor", "acct-1")]);
        Assert.False(vault.Items.ContainsKey(("ApiBalanceMonitor", "acct-1")));
    }

    [Fact]
    public async Task DeleteAsync_RemovesNewAndLegacy()
    {
        var (store, vault) = Create();
        vault.Items[("ApiMonitor", "acct-1")] = "sk-test-only-not-real-new";
        vault.Items[("ApiBalanceMonitor", "acct-1")] = "sk-test-only-not-real-legacy";

        await store.DeleteAsync("acct-1", CancellationToken.None);

        Assert.Empty(vault.Items);
    }

    [Fact]
    public void Contains_ReturnsTrue_WhenOnlyLegacyExists()
    {
        var (store, vault) = Create();
        vault.Items[("ApiBalanceMonitor", "acct-1")] = "sk-test-only-not-real-legacy";

        Assert.True(store.Contains("acct-1"));
    }
}
