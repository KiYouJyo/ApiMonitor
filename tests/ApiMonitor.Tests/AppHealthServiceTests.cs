using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v1.0.0：应用运行状况检查测试。21 项检查只读非敏感状态；
/// 单项失败/异常不阻断其他检查；结果不含敏感内容。
/// </summary>
public sealed class AppHealthServiceTests
{
    private static readonly string[] AllCheckIds =
    {
        "channel",
        "display-version",
        "package-version",
        "package-identity",
        "package-family",
        "architecture",
        "credential-locker",
        "accounts-file",
        "records-file",
        "settings-file",
        "provider-registry",
        "deepseek-provider",
        "openrouter-provider",
        "notification-registered",
        "notification-system",
        "tray",
        "startup-task",
        "scheduler",
        "windows",
        "last-query",
        "update-service",
    };

    [Fact]
    public async Task AllNormal_ReturnsAll21Checks_WithNoFailures()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out _, out _, out _);

        var results = await service.RunAsync(CancellationToken.None);

        Assert.Equal(AllCheckIds.OrderBy(x => x), results.Select(r => r.CheckId).OrderBy(x => x));
        Assert.DoesNotContain(results, r => r.Status == HealthStatus.Failed);
    }

    [Fact]
    public async Task CorruptAccountsFile_IsWarning_NotCrash()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, JsonAccountStore.FileName), "{ broken !!!");
        var service = CreateService(temp.Path, out _, out _, out _);

        var results = await service.RunAsync(CancellationToken.None);

        var check = Assert.Single(results, r => r.CheckId == "accounts-file");
        Assert.Equal(HealthStatus.Warning, check.Status);
        Assert.DoesNotContain(results, r => r.Status == HealthStatus.Failed);
    }

    [Fact]
    public async Task CredentialLockerUnavailable_IsFailed()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out _, out _, out _, secretAvailable: false);

        var results = await service.RunAsync(CancellationToken.None);

        var check = Assert.Single(results, r => r.CheckId == "credential-locker");
        Assert.Equal(HealthStatus.Failed, check.Status);
    }

    [Fact]
    public async Task NotificationNotRegistered_IsWarning_ButOtherChecksContinue()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out _, out _, out _, notificationRegistered: false);

        var results = await service.RunAsync(CancellationToken.None);

        Assert.Contains(results, r => r.CheckId == "notification-registered" && r.Status == HealthStatus.Warning);
        // 除通知相关检查外，其他检查仍然正常完成。
        Assert.Contains(results, r => r.CheckId == "channel" && r.Status == HealthStatus.Ok);
        Assert.Equal(AllCheckIds.Length, results.Count);
    }

    [Fact]
    public async Task StartupDisabledByUser_IsWarning()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out _, out _, out _, startupStatus: StartupTaskStatus.DisabledByUser);

        var results = await service.RunAsync(CancellationToken.None);

        var check = Assert.Single(results, r => r.CheckId == "startup-task");
        Assert.Equal(HealthStatus.Warning, check.Status);
    }

    [Fact]
    public async Task ProviderMissing_IsFailed()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out _, out _, out _, providerIds: Array.Empty<string>());

        var results = await service.RunAsync(CancellationToken.None);

        Assert.Contains(results, r => r.CheckId == "deepseek-provider" && r.Status == HealthStatus.Failed);
        Assert.Contains(results, r => r.CheckId == "openrouter-provider" && r.Status == HealthStatus.Failed);
    }

    [Fact]
    public async Task UpdateServiceMismatch_IsFailed()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out _, out _, out _, updateChannel: DistributionChannel.MicrosoftStore);

        var results = await service.RunAsync(CancellationToken.None);

        var check = Assert.Single(results, r => r.CheckId == "update-service");
        Assert.Equal(HealthStatus.Failed, check.Status);
    }

    [Fact]
    public async Task SingleCheckException_DoesNotBlockOtherChecks()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out _, out _, out _, notificationsReaderThrows: true);

        var results = await service.RunAsync(CancellationToken.None);

        Assert.Contains(results, r => r.CheckId == "notification-system" && r.Status == HealthStatus.Warning);
        Assert.Contains(results, r => r.CheckId == "channel" && r.Status == HealthStatus.Ok);
        Assert.Equal(AllCheckIds.Length, results.Count);
    }

    [Fact]
    public async Task Results_ContainNoSensitiveContent()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp.Path, out var accountManager, out _, out _);
        accountManager.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-sensitive",
            ProviderId = "deepseek",
            DisplayName = "敏感账户名",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        var results = await service.RunAsync(CancellationToken.None);
        string joined = string.Join("\n", results.Select(r => $"{r.CheckId}: {r.Message}"));

        Assert.DoesNotContain("acct-sensitive", joined);
        Assert.DoesNotContain("敏感账户名", joined);
        Assert.DoesNotContain("sk-", joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), joined);
    }

    private static AppHealthService CreateService(
        string dataDirectory,
        out FakeAccountManager accountManager,
        out ProviderRegistry registry,
        out FakeTrayIconService tray,
        bool secretAvailable = true,
        bool notificationRegistered = true,
        bool notificationsReaderThrows = false,
        StartupTaskStatus startupStatus = StartupTaskStatus.Disabled,
        IReadOnlyList<string>? providerIds = null,
        DistributionChannel updateChannel = DistributionChannel.Development)
    {
        accountManager = new FakeAccountManager();
        providerIds ??= new[] { "deepseek", "openrouter" };
        registry = new ProviderRegistry(providerIds.Select(id => (IApiBalanceProvider)new FakeProvider(id)));
        tray = new FakeTrayIconService();
        tray.IsActive = true;
        var channel = new DistributionChannelService("1.0.0", "1.0.0.1");
        var secrets = new FakeSecretStore();
        if (!secretAvailable)
        {
            secrets.Available = false;
        }

        var accountStore = new JsonAccountStore(dataDirectory);
        var snapshotStore = new JsonBalanceSnapshotStore(dataDirectory);
        var appearanceStore = new JsonAppearanceSettingsStore(dataDirectory);
        var notificationService = new FakeAppNotificationService();
        if (notificationRegistered)
        {
            notificationService.Register();
        }

        var scheduler = new FakeMonitoringScheduler();

        return new AppHealthService(
            channel,
            secrets,
            accountStore,
            snapshotStore,
            appearanceStore,
            registry,
            notificationService,
            async ct =>
            {
                if (notificationsReaderThrows)
                {
                    throw new IOException("模拟读取失败");
                }

                return true;
            },
            tray,
            new FakeStartupTask(status: startupStatus),
            scheduler,
            () => true,
            () => false,
            accountManager,
            new StubUpdateService(updateChannel));
    }

    private sealed class FakeProvider : IApiBalanceProvider
    {
        public FakeProvider(string providerId)
        {
            ProviderId = providerId;
            DisplayName = providerId;
            Info = new ProviderInfo(
                providerId,
                providerId,
                providerId,
                SupportsAccountBalance: true,
                SupportsKeyQuota: false,
                SupportedMetricKinds: Array.Empty<BalanceMetricKind>(),
                CredentialOptions: Array.Empty<ProviderCredentialOption>(),
                ApiKeyInputHint: string.Empty,
                HelpUrl: string.Empty,
                SupportsTestConnection: false);
        }

        public string ProviderId { get; }

        public string DisplayName { get; }

        public ProviderInfo Info { get; }

        public Task<BalanceQueryResult> QueryBalanceAsync(
            ApiAccount account,
            IReadOnlyDictionary<string, string> credentials,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("健康检查测试不执行真实查询。");
    }

    private sealed class StubUpdateService : IUpdateService
    {
        public StubUpdateService(DistributionChannel channel) => Channel = channel;

        public DistributionChannel Channel { get; }

        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new UpdateCheckResult { Status = UpdateCheckStatus.DevelopmentBuild });
    }

    private sealed class FakeStartupTask : IStartupTaskService
    {
        public FakeStartupTask(StartupTaskStatus status) => CachedStatus = status;

        public StartupTaskStatus? CachedStatus { get; }

        public Task<StartupTaskStatus> RefreshStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CachedStatus ?? StartupTaskStatus.Disabled);

        public Task<StartupTaskStatus> EnableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CachedStatus ?? StartupTaskStatus.Disabled);

        public Task<StartupTaskStatus> DisableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CachedStatus ?? StartupTaskStatus.Disabled);
    }
}
