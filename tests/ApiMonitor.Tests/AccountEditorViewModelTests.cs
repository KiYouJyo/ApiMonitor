using ApiMonitor.Models;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.Tests.TestHelpers;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class AccountEditorViewModelTests
{
    private static AccountEditorContext CreateContext(bool editing = false, bool hasCredential = false)
    {
        var manager = new FakeAccountManager();
        return new AccountEditorContext
        {
            AccountId = editing ? "acct-1" : null,
            Providers = manager.Providers,
            InitialProviderId = "deepseek",
            InitialDisplayName = editing ? "Test" : string.Empty,
            HasStoredCredential = hasCredential,
            InitialMonitoring = new MonitoringSettings(),
            CurrentMetrics = Array.Empty<BalanceMetric>(),
        };
    }

    [Fact]
    public void Constructor_DoesNotThrow_ForNewAndEditContexts()
    {
        var manager = new FakeAccountManager();

        var newVm = new AccountEditorViewModel(manager, CreateContext());
        var editVm = new AccountEditorViewModel(
            manager,
            CreateContext(editing: true, hasCredential: true));

        Assert.False(newVm.CanSave);
        Assert.False(newVm.CanTest);
        Assert.True(editVm.CanSave);
        Assert.True(editVm.CanTest);
    }

    [Fact]
    public void CanSave_RequiresDisplayNameAndApiKeyForNewAccount()
    {
        var vm = new AccountEditorViewModel(new FakeAccountManager(), CreateContext());

        vm.DisplayName = "My DeepSeek";
        Assert.False(vm.CanSave);
        Assert.False(vm.CanTest);

        vm.ApiKey = "sk-test-only";
        Assert.True(vm.CanSave);
        Assert.True(vm.CanTest);
    }

    [Fact]
    public void TryBuildResult_ReturnsNullKeyWhenKeepingExistingCredential()
    {
        var vm = new AccountEditorViewModel(
            new FakeAccountManager(),
            CreateContext(editing: true, hasCredential: true));

        bool ok = vm.TryBuildResult(out var result);

        Assert.True(ok);
        Assert.NotNull(result);
        Assert.True(result!.SaveRequested);
        Assert.Null(result.ApiKey);
        Assert.Equal("Test", result.DisplayName);
    }

    [Fact]
    public void NewAccount_DefaultsToAutoRefreshEnabledAnd30Minutes()
    {
        var vm = new AccountEditorViewModel(new FakeAccountManager(), CreateContext());
        vm.DisplayName = "Test";
        vm.ApiKey = "sk-test-only-not-real";

        Assert.True(vm.AutoRefreshEnabled);
        Assert.Equal(30, vm.RefreshIntervalMinutes);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void NegativeThreshold_IsRejected()
    {
        var manager = new FakeAccountManager();
        var context = new AccountEditorContext
        {
            Providers = manager.Providers,
            InitialProviderId = "deepseek",
            InitialDisplayName = "Test",
            HasStoredCredential = false,
            InitialMonitoring = new MonitoringSettings(),
            CurrentMetrics = new[] { TestMetrics.Cny(10m) },
        };
        var vm = new AccountEditorViewModel(manager, context);
        vm.ApiKey = "sk-test-only-not-real";
        var item = vm.ThresholdItems.Single();
        item.IsEnabled = true;
        item.ThresholdText = "-5";

        Assert.False(vm.CanSave);
        Assert.False(vm.TryBuildResult(out _));
        Assert.Contains("阈值金额", vm.ValidationMessage);
    }

    [Fact]
    public void ValidThreshold_ParsesDecimalAndSavesRule()
    {
        var manager = new FakeAccountManager();
        var context = new AccountEditorContext
        {
            Providers = manager.Providers,
            InitialProviderId = "deepseek",
            InitialDisplayName = "Test",
            HasStoredCredential = false,
            InitialMonitoring = new MonitoringSettings(),
            CurrentMetrics = new[] { TestMetrics.Cny(10m) },
        };
        var vm = new AccountEditorViewModel(manager, context);
        vm.ApiKey = "sk-test-only-not-real";
        var item = vm.ThresholdItems.Single();
        item.IsEnabled = true;
        item.ThresholdText = "20.50";

        Assert.True(vm.TryBuildResult(out var result));
        var rule = Assert.Single(result!.Monitoring.Thresholds);
        Assert.Equal("deepseek:CNY:total", rule.MetricId);
        Assert.Equal("CNY", rule.Unit);
        Assert.True(rule.IsEnabled);
        Assert.Equal(20.50m, rule.ThresholdAmount);
    }

    [Fact]
    public void NewCurrencyWithoutRule_CreatesNoRuleOnSave()
    {
        var manager = new FakeAccountManager();
        var context = new AccountEditorContext
        {
            Providers = manager.Providers,
            InitialProviderId = "deepseek",
            InitialDisplayName = "Test",
            HasStoredCredential = false,
            InitialMonitoring = new MonitoringSettings(),
            CurrentMetrics = new[] { TestMetrics.Usd(5m) },
        };
        var vm = new AccountEditorViewModel(manager, context);
        vm.ApiKey = "sk-test-only-not-real";

        Assert.True(vm.TryBuildResult(out var result));
        Assert.Empty(result!.Monitoring.Thresholds);
    }

    [Fact]
    public void NewAccount_WithoutBalances_ShowsThresholdEmptyHint()
    {
        var vm = new AccountEditorViewModel(new FakeAccountManager(), CreateContext());

        Assert.True(vm.ShowThresholdEmptyHint);
        Assert.Empty(vm.ThresholdItems);
    }

    [Fact]
    public async Task TestConnectionSuccess_PopulatesThresholdRowsFromBalances()
    {
        var manager = new FakeAccountManager
        {
            RefreshResult = BalanceQueryResult.Success(new BalanceSnapshot
            {
                SnapshotId = "snap-1",
                AccountId = "test",
                ProviderId = "deepseek",
                RetrievedAt = DateTimeOffset.UtcNow,
                Metrics = new[]
                {
                    TestMetrics.Cny(10.5m),
                    TestMetrics.Usd(2.25m),
                },
            }),
        };
        var vm = new AccountEditorViewModel(manager, CreateContext());
        vm.DisplayName = "Test";
        vm.ApiKey = "sk-test-only-not-real";

        await vm.TestCommand.ExecuteAsync(null);

        Assert.False(vm.ShowThresholdEmptyHint);
        Assert.Equal(2, vm.ThresholdItems.Count);
        Assert.Equal(10.5m, vm.ThresholdItems.Single(i => i.MetricId == "deepseek:CNY:total").CurrentAmount);
        Assert.Equal(2.25m, vm.ThresholdItems.Single(i => i.MetricId == "deepseek:USD:total").CurrentAmount);
    }

    [Fact]
    public async Task TestConnectionSuccess_UpdatesExistingRowTotal()
    {
        var manager = new FakeAccountManager
        {
            RefreshResult = BalanceQueryResult.Success(new BalanceSnapshot
            {
                SnapshotId = "snap-2",
                AccountId = "test",
                ProviderId = "deepseek",
                RetrievedAt = DateTimeOffset.UtcNow,
                Metrics = new[]
                {
                    TestMetrics.Cny(20m),
                },
            }),
        };
        var context = new AccountEditorContext
        {
            Providers = manager.Providers,
            InitialProviderId = "deepseek",
            InitialDisplayName = "Test",
            HasStoredCredential = false,
            InitialMonitoring = new MonitoringSettings(),
            CurrentMetrics = new[] { TestMetrics.Cny(10m) },
        };
        var vm = new AccountEditorViewModel(manager, context);
        vm.ApiKey = "sk-test-only-not-real";

        await vm.TestCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.ThresholdItems);
        Assert.Equal(20m, item.CurrentAmount);
        Assert.Equal("当前余额：20.00", item.CurrentBalanceLine);
    }

    [Fact]
    public async Task TestConnectionFailure_KeepsThresholdItemsAndHint()
    {
        var manager = new FakeAccountManager
        {
            RefreshResult = BalanceQueryResult.Failure(BalanceErrorKind.Unauthorized, "API Key 无效。"),
        };
        var context = new AccountEditorContext
        {
            Providers = manager.Providers,
            InitialProviderId = "deepseek",
            InitialDisplayName = "Test",
            HasStoredCredential = false,
            InitialMonitoring = new MonitoringSettings(),
            CurrentMetrics = new[] { TestMetrics.Cny(10m) },
        };
        var vm = new AccountEditorViewModel(manager, context);
        vm.ApiKey = "sk-test-only-not-real";

        await vm.TestCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.ThresholdItems);
        Assert.Equal(10m, item.CurrentAmount);
        Assert.False(vm.ShowThresholdEmptyHint);
    }

    [Fact]
    public async Task TestConnectionSuccess_WithEmptyBalances_KeepsHint()
    {
        var manager = new FakeAccountManager
        {
            RefreshResult = BalanceQueryResult.Success(new BalanceSnapshot
            {
                SnapshotId = "snap-3",
                AccountId = "test",
                ProviderId = "deepseek",
                RetrievedAt = DateTimeOffset.UtcNow,
                Metrics = Array.Empty<BalanceMetric>(),
            }),
        };
        var vm = new AccountEditorViewModel(manager, CreateContext());
        vm.DisplayName = "Test";
        vm.ApiKey = "sk-test-only-not-real";

        await vm.TestCommand.ExecuteAsync(null);

        Assert.True(vm.ShowThresholdEmptyHint);
        Assert.Empty(vm.ThresholdItems);
    }
}
