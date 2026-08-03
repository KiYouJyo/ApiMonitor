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

    [Fact]
    public async Task AccountFile_V2ToV3_MigratesCurrencyThresholdsToMetricIds()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "accounts.json");
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": 2,
              "accounts": [
                {
                  "accountId": "acct-keep",
                  "providerId": "deepseek",
                  "displayName": "我的账户",
                  "hasCredential": true,
                  "createdAtUtc": "2026-08-01T00:00:00Z",
                  "updatedAtUtc": "2026-08-01T01:00:00Z",
                  "monitoring": {
                    "autoRefreshEnabled": true,
                    "refreshIntervalMinutes": 30,
                    "thresholds": [
                      { "currency": "CNY", "isEnabled": true, "thresholdAmount": 10.00, "createdAtUtc": "2026-08-01T00:00:00Z", "updatedAtUtc": "2026-08-01T00:00:00Z" }
                    ]
                  }
                }
              ]
            }
            """);

        var store = new JsonAccountStore(temp.Path);
        var loaded = await store.LoadAsync(CancellationToken.None);

        var account = Assert.Single(loaded.Accounts);
        var rule = Assert.Single(account.Monitoring.Thresholds);
        Assert.Equal("deepseek:CNY:total", rule.MetricId);
        Assert.Equal("CNY 总余额", rule.DisplayName);
        Assert.Equal("CNY", rule.Unit);
        Assert.Equal(10.00m, rule.ThresholdAmount);
        Assert.True(rule.IsEnabled);
        Assert.Null(account.CredentialMode);
        Assert.Null(account.Notification.NotificationsEnabled);

        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"schemaVersion\": 3", json);
        Assert.Contains("deepseek:CNY:total", json);
        Assert.Single(Directory.GetFiles(temp.Path, "*.migrated-backup-*.json"));
    }

    [Fact]
    public async Task RecordsFile_V2ToV3_MigratesCurrencyBalancesToMetrics()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "balance-records.json");
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": 2,
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
                  },
                  "history": [
                    {
                      "id": "h1",
                      "accountId": "acct-keep",
                      "providerId": "deepseek",
                      "succeededAtUtc": "2026-08-02T08:00:05Z",
                      "source": "Manual",
                      "isAvailable": true,
                      "balances": [
                        { "currency": "CNY", "totalBalance": 110.00, "grantedBalance": 10.00, "toppedUpBalance": 100.00 }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

        var store = new JsonBalanceSnapshotStore(temp.Path);
        var loaded = await store.LoadAsync(CancellationToken.None);

        var record = Assert.Single(loaded.Records);
        Assert.NotNull(record.LastSuccessfulSnapshot);
        Assert.False(string.IsNullOrWhiteSpace(record.LastSuccessfulSnapshot!.SnapshotId));
        var metric = Assert.Single(record.LastSuccessfulSnapshot.Metrics);
        Assert.Equal("deepseek:CNY:total", metric.MetricId);
        Assert.Equal(110.00m, metric.AvailableAmount);
        Assert.Equal(10.00m, metric.GrantedAmount);
        Assert.Equal(100.00m, metric.ToppedUpAmount);

        var history = Assert.Single(record.History);
        Assert.Equal("deepseek:CNY:total", history.Metrics[0].MetricId);

        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"schemaVersion\": 3", json);
        Assert.Single(Directory.GetFiles(temp.Path, "*.migrated-backup-*.json"));
    }

    [Fact]
    public async Task NotificationSettings_MissingFile_DefaultsToNotificationsOff()
    {
        using var temp = new TempDirectory();
        var store = new JsonNotificationSettingsStore(temp.Path);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.False(settings.BalanceNotificationsEnabled);
        Assert.True(settings.RecoveryNotificationsEnabled);
        Assert.Equal(24, settings.DefaultRepeatIntervalHours);
    }
}
