using ApiMonitor.Services;
using Windows.Security.Credentials;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// Credential Locker 正常读写测试：仅使用内存假凭据存储，绝不访问真实 Credential Locker。
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
    public async Task SetAsync_WritesApiMonitorResource()
    {
        var (store, vault) = Create();

        await store.SetAsync("acct-1", "sk-test-only-not-real", CancellationToken.None);

        Assert.Equal("sk-test-only-not-real", vault.Items[("ApiMonitor", "acct-1")]);
    }

    [Fact]
    public async Task GetAsync_ReturnsSavedSecret()
    {
        var (store, vault) = Create();
        vault.Items[("ApiMonitor", "acct-1")] = "sk-test-only-not-real";

        var secret = await store.GetAsync("acct-1", CancellationToken.None);

        Assert.Equal("sk-test-only-not-real", secret);
    }

    [Fact]
    public async Task GetAsync_NoCredential_ReturnsNull()
    {
        var (store, _) = Create();

        var secret = await store.GetAsync("acct-missing", CancellationToken.None);

        Assert.Null(secret);
    }

    [Fact]
    public async Task SetAsync_ReplacesExistingSecret()
    {
        var (store, vault) = Create();
        vault.Items[("ApiMonitor", "acct-1")] = "sk-test-only-not-real-old";

        await store.SetAsync("acct-1", "sk-test-only-not-real-new", CancellationToken.None);

        Assert.Single(vault.Items);
        Assert.Equal("sk-test-only-not-real-new", vault.Items[("ApiMonitor", "acct-1")]);
    }

    [Fact]
    public async Task DeleteAsync_RemovesStoredSecret()
    {
        var (store, vault) = Create();
        vault.Items[("ApiMonitor", "acct-1")] = "sk-test-only-not-real";

        await store.DeleteAsync("acct-1", CancellationToken.None);

        Assert.Empty(vault.Items);
    }

    [Fact]
    public async Task DeleteAsync_WhenMissing_IsNoOp()
    {
        var (store, vault) = Create();

        await store.DeleteAsync("acct-missing", CancellationToken.None);

        Assert.Empty(vault.Items);
    }

    [Fact]
    public void Contains_ReturnsTrue_WhenCredentialExists()
    {
        var (store, vault) = Create();
        vault.Items[("ApiMonitor", "acct-1")] = "sk-test-only-not-real";

        Assert.True(store.Contains("acct-1"));
    }

    [Fact]
    public void Contains_ReturnsFalse_WhenMissing()
    {
        var (store, _) = Create();

        Assert.False(store.Contains("acct-missing"));
    }
}
