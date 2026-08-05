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
        Assert.Contains("3", content);
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

    [Fact]
    public async Task ProviderConfig_RoundTripsAndTeamIdIsStored()
    {
        using var temp = new TempDirectory();
        var store = new JsonAccountStore(temp.Path);
        var account = new ApiAccount
        {
            AccountId = "acct-xai",
            ProviderId = "xai",
            DisplayName = "xAI Team",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ProviderConfig = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["teamId"] = "65c1e471-205f-4566-9c5a-07198bcdf4ce",
            },
        };

        await store.SaveAsync(new[] { account }, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        var reloaded = Assert.Single(loaded.Accounts);
        Assert.Equal("65c1e471-205f-4566-9c5a-07198bcdf4ce", reloaded.ProviderConfig["teamId"]);

        // Team ID 属于非敏感账户配置，允许写入 accounts.json；密钥不得出现。
        string json = await File.ReadAllTextAsync(Path.Combine(temp.Path, "accounts.json"));
        Assert.Contains("teamId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("xai-management-key-secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task V070FileWithoutProviderConfig_LoadsWithEmptyConfig()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "accounts.json");
        const string v070Json = """
            {
              "schemaVersion": 3,
              "accounts": [
                {
                  "accountId": "acct-old",
                  "providerId": "deepseek",
                  "displayName": "旧账户",
                  "hasCredential": true,
                  "createdAtUtc": "2026-08-01T00:00:00Z",
                  "updatedAtUtc": "2026-08-01T00:00:00Z",
                  "credentialMode": null
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, v070Json);
        var store = new JsonAccountStore(temp.Path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        var account = Assert.Single(loaded.Accounts);
        Assert.Equal("acct-old", account.AccountId);
        Assert.Equal("deepseek", account.ProviderId);
        Assert.NotNull(account.ProviderConfig);
        Assert.Empty(account.ProviderConfig);
        Assert.True(account.HasCredential);
    }
}
