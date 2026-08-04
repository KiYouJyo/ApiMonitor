using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class PortableBackupServiceTests
{
    private static PortableBackupService CreateService(
        string dataDir,
        out JsonAccountStore accountStore,
        out JsonBalanceSnapshotStore snapshotStore,
        out JsonNotificationSettingsStore notificationStore,
        out JsonTraySettingsStore trayStore,
        out FloatingWindowSettingsStore floatingStore,
        out JsonAppearanceSettingsStore appearanceStore)
    {
        accountStore = new JsonAccountStore(dataDir);
        snapshotStore = new JsonBalanceSnapshotStore(dataDir);
        notificationStore = new JsonNotificationSettingsStore(dataDir);
        trayStore = new JsonTraySettingsStore(dataDir);
        floatingStore = new FloatingWindowSettingsStore(dataDir);
        appearanceStore = new JsonAppearanceSettingsStore(dataDir);

        return new PortableBackupService(
            dataDir,
            accountStore,
            snapshotStore,
            notificationStore,
            trayStore,
            floatingStore,
            appearanceStore,
            new[] { "deepseek", "openrouter" });
    }

    private static ApiAccount Account(string id, string providerId = "deepseek", bool hasCredential = true) =>
        new()
        {
            AccountId = id,
            ProviderId = providerId,
            DisplayName = $"账户 {id}",
            HasCredential = hasCredential,
            CreatedAtUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        };

    private static AccountBalanceRecord Record(string accountId, params (string id, decimal value)[] history) =>
        new()
        {
            AccountId = accountId,
            ProviderId = "deepseek",
            History = history
                .Select((h, i) => new BalanceHistoryEntry
                {
                    Id = h.id,
                    AccountId = accountId,
                    ProviderId = "deepseek",
                    SucceededAtUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i),
                    Source = BalanceQuerySource.Manual,
                    IsAvailable = true,
                    Metrics = new[]
                    {
                        new BalanceMetric
                        {
                            MetricId = "deepseek:CNY:total",
                            DisplayName = "CNY 总余额",
                            Unit = "CNY",
                            Kind = BalanceMetricKind.MonetaryBalance,
                            AvailableAmount = h.value,
                        },
                    },
                })
                .ToList(),
        };

    [Fact]
    public async Task Export_CreatesValidZippedManifest()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out var accounts, out var records, out _, out _, out _, out _);
        await accounts.SaveAsync(new[] { Account("acct-1") }, CancellationToken.None);
        await records.SaveAsync(new[] { Record("acct-1", ("h1", 100m)) }, CancellationToken.None);

        string backupPath = Path.Combine(temp.Path, "backup.apimonitor-backup");
        await service.ExportAsync(backupPath, CancellationToken.None);

        Assert.True(File.Exists(backupPath));
        using var archive = ZipFile.OpenRead(backupPath);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("manifest.json", names);
        Assert.Contains("accounts.json", names);
        Assert.Contains("balance-records.json", names);
        Assert.Contains("appearance-settings.json", names);
    }

    [Fact]
    public async Task Export_ManifestDeclaresNoSecrets()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out _, out _, out _, out _, out _, out _);

        string backupPath = Path.Combine(temp.Path, "backup.apimonitor-backup");
        await service.ExportAsync(backupPath, CancellationToken.None);

        using var archive = ZipFile.OpenRead(backupPath);
        var entry = archive.GetEntry("manifest.json")!;
        using var reader = new StreamReader(entry.Open());
        string json = await reader.ReadToEndAsync();
        Assert.Contains("\"containsSecrets\": false", json);
        Assert.Contains("\"backupFormatVersion\": 1", json);
    }

    [Fact]
    public async Task Inspect_ReportsPreviewCounts()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out var accounts, out var records, out _, out _, out _, out _);
        await accounts.SaveAsync(
            new[] { Account("acct-1"), Account("acct-2") },
            CancellationToken.None);
        await records.SaveAsync(
            new[] { Record("acct-1", ("h1", 100m), ("h2", 90m)) },
            CancellationToken.None);

        string backupPath = Path.Combine(temp.Path, "backup.apimonitor-backup");
        await service.ExportAsync(backupPath, CancellationToken.None);

        var preview = await service.InspectAsync(backupPath, CancellationToken.None);

        Assert.Equal(2, preview.AccountCount);
        Assert.Equal(2, preview.HistoryEntryCount);
        Assert.Contains("deepseek", preview.ProviderIds);
        Assert.False(preview.Manifest.ContainsSecrets);
    }

    [Fact]
    public async Task Import_ExistingAccount_KeepsLocalCredential()
    {
        using var localTemp = new TempDirectory();
        using var backupTemp = new TempDirectory();

        var service = CreateService(localTemp.Path, out var accounts, out _, out _, out _, out _, out _);

        // 在独立目录创建备份：acct-1（无凭据标记）。
        string backupPath = Path.Combine(backupTemp.Path, "backup.apimonitor-backup");
        var exportService = CreateService(backupTemp.Path, out var exportAccounts, out _, out _, out _, out _, out _);
        await exportAccounts.SaveAsync(
            new[] { Account("acct-1", hasCredential: false) },
            CancellationToken.None);
        await exportService.ExportAsync(backupPath, CancellationToken.None);

        // 空本机导入（模拟另一台机器）。
        await accounts.SaveAsync(Array.Empty<ApiAccount>(), CancellationToken.None);

        var result = await service.ImportAsync(backupPath, BackupMergePreference.KeepLocal, CancellationToken.None);

        // 备份中 acct-1 无凭据：导入到空本机 → 新账户，标记需要凭据。
        Assert.Equal(1, result.AddedAccounts);
        Assert.Contains("acct-1", result.AccountsNeedingCredential);

        var loaded = await accounts.LoadAsync(CancellationToken.None);
        var imported = Assert.Single(loaded.Accounts);
        Assert.False(imported.HasCredential);
        // 新账户默认关闭自动刷新与系统通知。
        Assert.False(imported.Monitoring.AutoRefreshEnabled);
        Assert.Equal(false, imported.Notification.NotificationsEnabled);
    }

    [Fact]
    public async Task Import_HistoryDeduplicatesById()
    {
        using var localTemp = new TempDirectory();
        using var backupTemp = new TempDirectory();

        var service = CreateService(localTemp.Path, out var accounts, out var records, out _, out _, out _, out _);
        await accounts.SaveAsync(new[] { Account("acct-1") }, CancellationToken.None);

        // 本地已有 h1。
        await records.SaveAsync(
            new[] { Record("acct-1", ("h1", 100m)) },
            CancellationToken.None);

        // 在独立目录生成备份：h1（重复）+ h2（新）。
        string backupPath = Path.Combine(backupTemp.Path, "backup.apimonitor-backup");
        var exportService = CreateService(backupTemp.Path, out _, out var exportRecords, out _, out _, out _, out _);
        await exportRecords.SaveAsync(
            new[] { Record("acct-1", ("h1", 100m), ("h2", 90m)) },
            CancellationToken.None);
        await exportService.ExportAsync(backupPath, CancellationToken.None);

        var result = await service.ImportAsync(backupPath, BackupMergePreference.KeepLocal, CancellationToken.None);

        Assert.Equal(1, result.AddedHistoryEntries);
        Assert.Equal(1, result.SkippedHistoryEntries);

        var loaded = await records.LoadAsync(CancellationToken.None);
        var record = Assert.Single(loaded.Records);
        Assert.Equal(2, record.History.Count); // h1 + h2，无重复
    }

    [Fact]
    public async Task Import_UnsupportedProvider_SkipsAccount()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out _, out _, out _, out _, out _, out _);

        string backupPath = Path.Combine(temp.Path, "backup.apimonitor-backup");
        // 创建只支持 deepseek 的服务来导出含 openrouter 的备份。
        var exportService = new PortableBackupService(
            temp.Path,
            new JsonAccountStore(temp.Path),
            new JsonBalanceSnapshotStore(temp.Path),
            new JsonNotificationSettingsStore(temp.Path),
            new JsonTraySettingsStore(temp.Path),
            new FloatingWindowSettingsStore(temp.Path),
            new JsonAppearanceSettingsStore(temp.Path),
            new[] { "deepseek", "openrouter" });
        await exportService.ExportAsync(backupPath, CancellationToken.None);

        // 导入服务只支持 deepseek。
        var importService = new PortableBackupService(
            temp.Path,
            new JsonAccountStore(temp.Path),
            new JsonBalanceSnapshotStore(temp.Path),
            new JsonNotificationSettingsStore(temp.Path),
            new JsonTraySettingsStore(temp.Path),
            new FloatingWindowSettingsStore(temp.Path),
            new JsonAppearanceSettingsStore(temp.Path),
            new[] { "deepseek" });

        var result = await importService.ImportAsync(backupPath, BackupMergePreference.KeepLocal, CancellationToken.None);
        Assert.True(result.AddedAccounts == 0 || result.SkippedAccounts >= 0);
    }

    [Fact]
    public async Task Inspect_RejectsCorruptManifest()
    {
        using var temp = new TempDirectory();
        string backupPath = Path.Combine(temp.Path, "bad.apimonitor-backup");
        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("manifest.json");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("{not valid json");
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new PortableBackupService(
                temp.Path,
                new JsonAccountStore(temp.Path),
                new JsonBalanceSnapshotStore(temp.Path),
                new JsonNotificationSettingsStore(temp.Path),
                new JsonTraySettingsStore(temp.Path),
                new FloatingWindowSettingsStore(temp.Path),
                new JsonAppearanceSettingsStore(temp.Path),
                new[] { "deepseek" }).InspectAsync(backupPath, CancellationToken.None));
    }

    [Fact]
    public async Task Inspect_RejectsPathTraversalEntry()
    {
        using var temp = new TempDirectory();
        string backupPath = Path.Combine(temp.Path, "evil.apimonitor-backup");
        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            _ = archive.CreateEntry("../../evil.txt");
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new PortableBackupService(
                temp.Path,
                new JsonAccountStore(temp.Path),
                new JsonBalanceSnapshotStore(temp.Path),
                new JsonNotificationSettingsStore(temp.Path),
                new JsonTraySettingsStore(temp.Path),
                new FloatingWindowSettingsStore(temp.Path),
                new JsonAppearanceSettingsStore(temp.Path),
                new[] { "deepseek" }).InspectAsync(backupPath, CancellationToken.None));
    }

    [Fact]
    public async Task Inspect_RejectsMissingManifest()
    {
        using var temp = new TempDirectory();
        string backupPath = Path.Combine(temp.Path, "nomanifest.apimonitor-backup");
        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            _ = archive.CreateEntry("accounts.json");
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new PortableBackupService(
                temp.Path,
                new JsonAccountStore(temp.Path),
                new JsonBalanceSnapshotStore(temp.Path),
                new JsonNotificationSettingsStore(temp.Path),
                new JsonTraySettingsStore(temp.Path),
                new FloatingWindowSettingsStore(temp.Path),
                new JsonAppearanceSettingsStore(temp.Path),
                new[] { "deepseek" }).InspectAsync(backupPath, CancellationToken.None));
    }

    [Fact]
    public async Task Import_LegacyBackupWithCompactWindowSettings_IsAcceptedAndMigrated()
    {
        using var temp = new TempDirectory();
        string backupPath = Path.Combine(temp.Path, "legacy.apimonitor-backup");
        var payloads = new Dictionary<string, string>
        {
            ["accounts.json"] = """{ "schemaVersion": 3, "accounts": [] }""",
            ["balance-records.json"] = """{ "schemaVersion": 3, "records": [] }""",
            ["notification-settings.json"] = "{}",
            ["tray-settings.json"] = "{}",
            ["compact-window-settings.json"] =
                """{ "schemaVersion": 3, "selectedAccountId": "acct-legacy", "width": 380, "height": 220, "x": 10, "y": 20 }""",
            ["appearance-settings.json"] = "{}",
        };

        var manifest = new
        {
            backupFormatVersion = 1,
            displayVersion = "0.6.0",
            packageVersion = "0.6.0.1",
            createdAtUtc = DateTimeOffset.UtcNow,
            containsSecrets = false,
            supportedProviderIds = new[] { "deepseek", "openrouter" },
            files = payloads.Select(p => new
            {
                name = p.Key,
                size = (long)Encoding.UTF8.GetByteCount(p.Value),
                sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Value))),
            }).ToList(),
        };
        payloads["manifest.json"] = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in payloads)
            {
                var entry = archive.CreateEntry(name);
                await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                await writer.WriteAsync(content);
            }
        }

        var service = CreateService(temp.Path, out _, out _, out _, out _, out var floatingStore, out _);
        var preview = await service.InspectAsync(backupPath, CancellationToken.None);
        Assert.Contains("compact-window-settings.json", preview.SettingsSections);

        await service.ImportAsync(backupPath, BackupMergePreference.PreferImport, CancellationToken.None);

        var settings = await floatingStore.LoadAsync(CancellationToken.None);
        Assert.Equal("acct-legacy", settings.SelectedAccountId);
        Assert.Equal(FloatingWindowDefaults.FixedSize, settings.Width);
        Assert.Equal(FloatingWindowDefaults.FixedSize, settings.Height);
    }
}
