using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Tests.TestDoubles;
using ApiBalanceMonitor.ViewModels;
using Xunit;

namespace ApiBalanceMonitor.Tests;

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
            CurrentBalances = Array.Empty<BalanceAmount>(),
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
            CurrentBalances = new[]
            {
                new BalanceAmount { Currency = "CNY", TotalBalance = 10m, GrantedBalance = 0m, ToppedUpBalance = 10m },
            },
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
            CurrentBalances = new[]
            {
                new BalanceAmount { Currency = "CNY", TotalBalance = 10m, GrantedBalance = 0m, ToppedUpBalance = 10m },
            },
        };
        var vm = new AccountEditorViewModel(manager, context);
        vm.ApiKey = "sk-test-only-not-real";
        var item = vm.ThresholdItems.Single();
        item.IsEnabled = true;
        item.ThresholdText = "20.50";

        Assert.True(vm.TryBuildResult(out var result));
        var rule = Assert.Single(result!.Monitoring.Thresholds);
        Assert.Equal("CNY", rule.Currency);
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
            CurrentBalances = new[]
            {
                new BalanceAmount { Currency = "USD", TotalBalance = 5m, GrantedBalance = 0m, ToppedUpBalance = 5m },
            },
        };
        var vm = new AccountEditorViewModel(manager, context);
        vm.ApiKey = "sk-test-only-not-real";

        Assert.True(vm.TryBuildResult(out var result));
        Assert.Empty(result!.Monitoring.Thresholds);
    }
}
