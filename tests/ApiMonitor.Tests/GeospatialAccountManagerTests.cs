using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using System.IO.Compression;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v0.9.0：AccountManager 地理行为（间隔下限、失败历史、凭据槽位、备份安全）。
/// </summary>
public sealed class GeospatialAccountManagerTests
{
    private static ProviderRegistry Registry() =>
        new(new IApiBalanceProvider[]
        {
            new DeepSeekBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new AmapBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new SuperMapIServerProvider(FakeHttpRequestService.Returning("{}")),
        });

    private static (AccountManager Manager, JsonAccountStore AccountStore, JsonBalanceSnapshotStore SnapshotStore, FakeSecretStore Secrets)
        CreateManager(TempDirectory temp)
    {
        var accountStore = new JsonAccountStore(temp.Path);
        var snapshotStore = new JsonBalanceSnapshotStore(temp.Path);
        var secrets = new FakeSecretStore();
        var manager = new AccountManager(
            accountStore,
            snapshotStore,
            secrets,
            Registry(),
            new AppLog(temp.Path),
            new FakeTimeProvider());
        return (manager, accountStore, snapshotStore, secrets);
    }

    private static ApiAccount AmapAccount() =>
        new()
        {
            AccountId = "acct-amap",
            ProviderId = "amap",
            DisplayName = "AMap",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CredentialSlots = new Dictionary<string, bool>
            {
                [CredentialSlots.Primary] = true,
                [CredentialSlots.Secret] = true,
            },
        };

    [Fact]
    public async Task SaveGeospatialAccount_RejectsTooFrequentInterval()
    {
        using var temp = new TempDirectory();
        var (manager, _, _, _) = CreateManager(temp);
        await manager.LoadAsync(CancellationToken.None);

        var monitoring = new MonitoringSettings
        {
            AutoRefreshEnabled = true,
            RefreshIntervalMinutes = 5,
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.SaveAccountAsync(
                null,
                "amap",
                "AMap",
                "key",
                null,
                monitoring,
                CancellationToken.None));
    }

    [Fact]
    public async Task SaveGeospatialAccount_AcceptsOneHourInterval()
    {
        using var temp = new TempDirectory();
        var (manager, _, _, _) = CreateManager(temp);
        await manager.LoadAsync(CancellationToken.None);

        var monitoring = new MonitoringSettings
        {
            AutoRefreshEnabled = true,
            RefreshIntervalMinutes = 60,
        };

        var account = await manager.SaveAccountAsync(
            null,
            "amap",
            "AMap",
            "key",
            null,
            monitoring,
            CancellationToken.None,
            credentialSlots: new Dictionary<string, string>
            {
                [CredentialSlots.Secret] = "sk-value",
            });

        Assert.True(account.CredentialSlots.ContainsKey(CredentialSlots.Primary));
        Assert.True(account.CredentialSlots.ContainsKey(CredentialSlots.Secret));
    }

    [Fact]
    public async Task FailedGeospatialRefresh_RecordsFailureHistory()
    {
        using var temp = new TempDirectory();
        var (manager, accountStore, snapshotStore, secrets) = CreateManager(temp);
        var amap = new AmapBalanceProvider(FakeHttpRequestService.Throwing<HttpRequestException>());
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { amap });
        var manager2 = new AccountManager(
            accountStore,
            snapshotStore,
            secrets,
            registry,
            new AppLog(temp.Path),
            new FakeTimeProvider());
        await manager2.LoadAsync(CancellationToken.None);
        var account = AmapAccount();
        await accountStore.SaveAsync(new[] { account }, CancellationToken.None);
        await secrets.SetAsync(account.AccountId, "key", CancellationToken.None);
        await manager2.LoadAsync(CancellationToken.None);

        var result = await manager2.RefreshAccountAsync(
            account.AccountId,
            BalanceQuerySource.Automatic,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var record = await manager2.GetRecordAsync(account.AccountId, CancellationToken.None);
        var history = Assert.Single(record!.History);
        Assert.False(history.IsAvailable);
        Assert.Contains(history.Metrics, m =>
            m.MetricId == "amap:service.availability"
            && m.StatusValue == nameof(GeospatialStatus.NetworkUnavailable));
    }

    [Fact]
    public async Task FailedAiRefresh_DoesNotRecordHistory()
    {
        using var temp = new TempDirectory();
        var (manager, accountStore, snapshotStore, secrets) = CreateManager(temp);
        var deepSeek = new DeepSeekBalanceProvider(FakeHttpRequestService.Throwing<HttpRequestException>());
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { deepSeek });
        var manager2 = new AccountManager(
            accountStore,
            snapshotStore,
            secrets,
            registry,
            new AppLog(temp.Path),
            new FakeTimeProvider());
        await manager2.LoadAsync(CancellationToken.None);
        var account = new ApiAccount
        {
            AccountId = "acct-ds",
            ProviderId = "deepseek",
            DisplayName = "DS",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await accountStore.SaveAsync(new[] { account }, CancellationToken.None);
        await secrets.SetAsync(account.AccountId, "key", CancellationToken.None);
        await manager2.LoadAsync(CancellationToken.None);

        var result = await manager2.RefreshAccountAsync(
            account.AccountId,
            BalanceQuerySource.Automatic,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var record = await manager2.GetRecordAsync(account.AccountId, CancellationToken.None);
        Assert.Empty(record!.History);
    }

    [Fact]
    public async Task AccountJson_StoresSlotPresence_NotValues()
    {
        using var temp = new TempDirectory();
        var (manager, accountStore, _, secrets) = CreateManager(temp);
        await manager.LoadAsync(CancellationToken.None);
        var saved = await manager.SaveAccountAsync(
            null,
            "amap",
            "AMap",
            "top-secret-key",
            null,
            new MonitoringSettings { AutoRefreshEnabled = false, RefreshIntervalMinutes = 360 },
            CancellationToken.None,
            credentialSlots: new Dictionary<string, string>
            {
                [CredentialSlots.Secret] = "top-secret-sk",
            });

        string json = await File.ReadAllTextAsync(Path.Combine(temp.Path, "accounts.json"));
        Assert.DoesNotContain("top-secret-key", json);
        Assert.DoesNotContain("top-secret-sk", json);
        Assert.Contains("credentialSlots", json);
        Assert.Equal("top-secret-key", await secrets.GetAsync(saved.AccountId, CancellationToken.None));
        Assert.Equal("top-secret-sk", await secrets.GetAsync(saved.AccountId, CancellationToken.None, CredentialSlots.Secret));
    }

    [Fact]
    public async Task Backup_ContainsBaseUrlAndSlotPresence_ButNeverSecretValues()
    {
        using var temp = new TempDirectory();
        var (manager, accountStore, snapshotStore, secrets) = CreateManager(temp);
        await manager.LoadAsync(CancellationToken.None);
        await manager.SaveAccountAsync(
            null,
            "supermap-iserver",
            "iServer",
            null,
            null,
            new MonitoringSettings { AutoRefreshEnabled = false, RefreshIntervalMinutes = 30 },
            CancellationToken.None,
            providerConfig: new Dictionary<string, string>
            {
                [SuperMapIServerProvider.BaseUrlField] = "https://gis.example.test:8090",
                [SuperMapIServerProvider.ExpectedServiceField] = "map-world",
            },
            credentialSlots: new Dictionary<string, string>
            {
                [CredentialSlots.QueryToken] = "super-secret-iserver-token",
            });

        var backupService = new PortableBackupService(
            temp.Path,
            accountStore,
            snapshotStore,
            new JsonNotificationSettingsStore(temp.Path),
            new JsonTraySettingsStore(temp.Path),
            new FloatingWindowSettingsStore(temp.Path),
            new JsonAppearanceSettingsStore(temp.Path),
            new[] { "supermap-iserver" });
        string backupPath = Path.Combine(temp.Path, "backup.apimonitor-backup");
        await backupService.ExportAsync(backupPath, CancellationToken.None);

        using var archive = ZipFile.OpenRead(backupPath);
        var entry = archive.GetEntry("accounts.json")!;
        using var reader = new StreamReader(entry.Open());
        string json = await reader.ReadToEndAsync();

        Assert.DoesNotContain("super-secret-iserver-token", json);
        Assert.Contains("gis.example.test:8090", json);
        Assert.Contains("map-world", json);
        Assert.Contains("credentialSlots", json);
    }
}
