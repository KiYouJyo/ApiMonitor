using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>v0.1.0（schemaVersion 1）→ v0.2.0（schemaVersion 2）数据迁移测试。</summary>
public sealed class DataMigrationTests
{
    [Fact]
    public async Task AccountFile_V1ToV2_KeepsIdsAndAddsMonitoringDefaults()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "accounts.json");
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": 1,
              "accounts": [
                {
                  "accountId": "acct-keep",
                  "providerId": "deepseek",
                  "displayName": "我的账户",
                  "hasCredential": true,
                  "createdAtUtc": "2026-08-01T00:00:00Z",
                  "updatedAtUtc": "2026-08-01T01:00:00Z"
                }
              ]
            }
            """);

        var store = new JsonAccountStore(temp.Path);
        var loaded = await store.LoadAsync(CancellationToken.None);

        var account = Assert.Single(loaded.Accounts);
        Assert.Equal("acct-keep", account.AccountId);
        Assert.Equal("deepseek", account.ProviderId);
        Assert.Equal("我的账户", account.DisplayName);
        Assert.True(account.Monitoring.AutoRefreshEnabled);
        Assert.Equal(30, account.Monitoring.RefreshIntervalMinutes);
        Assert.Empty(account.Monitoring.Thresholds);

        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"schemaVersion\": 3", json);
        Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordsFile_V1ToV2_MigratesSnapshotAsFirstHistoryEntry_Idempotent()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "balance-records.json");
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": 1,
              "records": [
                {
                  "accountId": "acct-keep",
                  "providerId": "deepseek",
                  "lastQueryAttemptAt": "2026-08-02T08:00:00Z",
                  "lastQuerySuccessAt": "2026-08-02T08:00:05Z",
                  "lastSuccessfulSnapshot": {
                    "accountId": "acct-keep",
                    "providerId": "deepseek",
                    "isAvailable": true,
                    "retrievedAt": "2026-08-02T08:00:05Z",
                    "balances": [
                      { "currency": "CNY", "totalBalance": 110.00, "grantedBalance": 10.00, "toppedUpBalance": 100.00 }
                    ]
                  }
                }
              ]
            }
            """);

        var store = new JsonBalanceSnapshotStore(temp.Path);
        var first = await store.LoadAsync(CancellationToken.None);
        var record = Assert.Single(first.Records);
        Assert.Equal("acct-keep", record.AccountId);
        Assert.NotNull(record.LastSuccessfulSnapshot);
        var migrated = Assert.Single(record.History);
        Assert.Equal(BalanceQuerySource.Manual, migrated.Source);
        Assert.Equal("deepseek:CNY:total", migrated.Metrics[0].MetricId);
        Assert.Equal("CNY", migrated.Metrics[0].Unit);
        Assert.Equal(110.00m, migrated.Metrics[0].AvailableAmount);
        Assert.Equal(new DateTimeOffset(2026, 8, 2, 8, 0, 5, TimeSpan.Zero), migrated.SucceededAtUtc);

        // 重复加载（已升级为 v2）不会重复生成历史记录。
        var second = await store.LoadAsync(CancellationToken.None);
        var recordAgain = Assert.Single(second.Records);
        Assert.Single(recordAgain.History);

        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"schemaVersion\": 3", json);
        Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorruptAccountFile_IsBackedUpAndAppCanContinue()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "accounts.json");
        await File.WriteAllTextAsync(path, "{ broken json !!!");

        var store = new JsonAccountStore(temp.Path);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(loaded.Accounts);
        Assert.NotNull(loaded.RecoveryMessage);
        Assert.Contains("备份", loaded.RecoveryMessage);
        Assert.Single(Directory.GetFiles(temp.Path, "*.corrupt-*.json"));
    }

    [Fact]
    public async Task MigratedV1Files_AreBackedUpBeforeRewrite()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "accounts.json");
        await File.WriteAllTextAsync(path, """
            { "schemaVersion": 1, "accounts": [ { "accountId": "acct-a", "providerId": "deepseek", "displayName": "A", "hasCredential": false, "createdAtUtc": "2026-08-01T00:00:00Z", "updatedAtUtc": "2026-08-01T00:00:00Z" } ] }
            """);

        var store = new JsonAccountStore(temp.Path);
        await store.LoadAsync(CancellationToken.None);

        Assert.Single(Directory.GetFiles(temp.Path, "*.migrated-backup-*.json"));
    }
}
