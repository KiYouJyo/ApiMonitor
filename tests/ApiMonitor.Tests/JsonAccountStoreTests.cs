using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class JsonAccountStoreTests
{
    [Fact]
    public async Task SaveAndReload_RoundTripsAccounts()
    {
        using var temp = new TempDirectory();
        var store = new JsonAccountStore(temp.Path);
        var account = new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "我的 DeepSeek",
            HasCredential = true,
            CreatedAtUtc = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 8, 2, 1, 2, 3, TimeSpan.Zero),
        };

        await store.SaveAsync(new[] { account }, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded.RecoveryMessage);
        var reloaded = Assert.Single(loaded.Accounts);
        Assert.Equal("acct-1", reloaded.AccountId);
        Assert.Equal("deepseek", reloaded.ProviderId);
        Assert.Equal("我的 DeepSeek", reloaded.DisplayName);
        Assert.True(reloaded.HasCredential);
        Assert.Equal(account.CreatedAtUtc, reloaded.CreatedAtUtc);
        Assert.Equal(account.UpdatedAtUtc, reloaded.UpdatedAtUtc);
    }

    [Fact]
    public async Task MissingFile_ReturnsEmptyList()
    {
        using var temp = new TempDirectory();
        var store = new JsonAccountStore(temp.Path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(loaded.Accounts);
        Assert.Null(loaded.RecoveryMessage);
    }

    [Fact]
    public async Task FileContainsSchemaVersion()
    {
        using var temp = new TempDirectory();
        var store = new JsonAccountStore(temp.Path);

        await store.SaveAsync(Array.Empty<ApiAccount>(), CancellationToken.None);
        string content = await File.ReadAllTextAsync(Path.Combine(temp.Path, "accounts.json"));

        Assert.Contains("schemaVersion", content);
        Assert.Contains("2", content);
    }

    [Fact]
    public async Task ApiKey_IsNeverWrittenToAccountJson()
    {
        const string secret = "sk-super-secret-abc123";
        using var temp = new TempDirectory();
        var store = new JsonAccountStore(temp.Path);
        var account = new ApiAccount
        {
            AccountId = "acct-1",
            ProviderId = "deepseek",
            DisplayName = "Test",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        await store.SaveAsync(new[] { account }, CancellationToken.None);
        string content = await File.ReadAllTextAsync(Path.Combine(temp.Path, "accounts.json"));

        Assert.DoesNotContain(secret, content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorruptFile_ReturnsRecoveryMessageAndBacksUp()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "accounts.json");
        await File.WriteAllTextAsync(path, "{ not valid json !!!");

        var store = new JsonAccountStore(temp.Path);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(loaded.Accounts);
        Assert.NotNull(loaded.RecoveryMessage);
        Assert.Contains("备份", loaded.RecoveryMessage);
        Assert.Single(Directory.GetFiles(temp.Path, "*.corrupt-*.json"));
    }

    [Fact]
    public async Task UnsupportedSchemaVersion_IsResetWithMessage()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "accounts.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new { schemaVersion = 999, accounts = new object[0] }));

        var store = new JsonAccountStore(temp.Path);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(loaded.Accounts);
        Assert.NotNull(loaded.RecoveryMessage);
        Assert.Contains("版本", loaded.RecoveryMessage);
    }
}
