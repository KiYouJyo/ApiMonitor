using ApiMonitor.Models;
using ApiMonitor.Services;
using Windows.Security.Credentials;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v0.9.0 多槽位凭据测试：旧 primary 键保持可读，新槽位独立存储。
/// </summary>
public sealed class CredentialSlotsTests
{
    private sealed class FakeVault : IPasswordVaultAdapter
    {
        public Dictionary<(string Resource, string User), string> Items { get; } = new();

        public PasswordCredential? Retrieve(string resource, string userName) =>
            Items.TryGetValue((resource, userName), out var password)
                ? new PasswordCredential(resource, userName, password)
                : null;

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
    public async Task PrimarySlot_KeepsLegacyUserNameKey()
    {
        var (store, vault) = Create();

        await store.SetAsync("acct-1", "primary-secret", CancellationToken.None);

        Assert.True(vault.Items.ContainsKey(("ApiMonitor", "acct-1")));
        Assert.Equal("primary-secret", await store.GetAsync("acct-1", CancellationToken.None));
    }

    [Fact]
    public async Task SecretAndQueryTokenSlots_StoreIndependently()
    {
        var (store, vault) = Create();

        await store.SetAsync("acct-1", "key-value", CancellationToken.None, CredentialSlots.Primary);
        await store.SetAsync("acct-1", "sk-value", CancellationToken.None, CredentialSlots.Secret);
        await store.SetAsync("acct-1", "tk-value", CancellationToken.None, CredentialSlots.QueryToken);

        Assert.Equal("key-value", await store.GetAsync("acct-1", CancellationToken.None, CredentialSlots.Primary));
        Assert.Equal("sk-value", await store.GetAsync("acct-1", CancellationToken.None, CredentialSlots.Secret));
        Assert.Equal("tk-value", await store.GetAsync("acct-1", CancellationToken.None, CredentialSlots.QueryToken));
        Assert.Equal(3, vault.Items.Count);
    }

    [Fact]
    public async Task LegacyEntry_RemainsReadableAsPrimary()
    {
        var (store, vault) = Create();
        vault.Items[("ApiMonitor", "acct-1")] = "legacy-secret";

        var value = await store.GetAsync("acct-1", CancellationToken.None);

        Assert.Equal("legacy-secret", value);
    }

    [Fact]
    public async Task GetPresentSlots_ReportsStoredSlotsOnly()
    {
        var (store, _) = Create();
        await store.SetAsync("acct-1", "a", CancellationToken.None, CredentialSlots.Primary);
        await store.SetAsync("acct-1", "b", CancellationToken.None, CredentialSlots.BearerToken);

        var present = store.GetPresentSlots("acct-1");

        Assert.Contains(CredentialSlots.Primary, present);
        Assert.Contains(CredentialSlots.BearerToken, present);
        Assert.DoesNotContain(CredentialSlots.Password, present);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAllSlots()
    {
        var (store, vault) = Create();
        await store.SetAsync("acct-1", "a", CancellationToken.None, CredentialSlots.Primary);
        await store.SetAsync("acct-1", "b", CancellationToken.None, CredentialSlots.Username);

        await store.DeleteAsync("acct-1", CancellationToken.None);

        Assert.Empty(vault.Items);
    }
}
