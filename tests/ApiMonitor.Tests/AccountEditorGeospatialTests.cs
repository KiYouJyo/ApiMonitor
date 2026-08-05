using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v0.9.0：账户编辑对话框的地理/GIS 默认值与多槽位凭据测试。
/// </summary>
public sealed class AccountEditorGeospatialTests
{
    private static readonly AmapBalanceProvider Amap = new(FakeHttpRequestService.Returning("{}"));

    private static readonly OgcServiceProvider Ogc = new(FakeHttpRequestService.Returning("{}"));

    private static AccountEditorContext Context(params ProviderInfo[] providers) =>
        new()
        {
            Providers = providers.Length > 0 ? providers : new[] { Amap.Info },
            InitialProviderId = providers.Length > 0 ? providers[0].ProviderId : "amap",
            InitialDisplayName = string.Empty,
            HasStoredCredential = false,
            InitialMonitoring = new MonitoringSettings(),
            CurrentMetrics = Array.Empty<BalanceMetric>(),
        };

    [Fact]
    public void NewMapAccount_DefaultsAutoRefreshOff_WithSixHourInterval()
    {
        var vm = new AccountEditorViewModel(new FakeAccountManager(), Context(Amap.Info));

        Assert.False(vm.AutoRefreshEnabled);
        Assert.Equal(MonitoringIntervals.GeospatialDefaultMinutes, vm.RefreshIntervalMinutes);
        Assert.Equal(2, vm.NotificationEnabledIndex); // 关闭通知
        Assert.True(vm.ShowProbeConsumesQuotaHint);
        Assert.DoesNotContain(5, vm.RefreshIntervals);
        Assert.Contains(60, vm.RefreshIntervals);
    }

    [Fact]
    public void NewGisServerAccount_DefaultsNotificationsOff_KeepsAutoRefreshOn()
    {
        var vm = new AccountEditorViewModel(new FakeAccountManager(), Context(Ogc.Info));

        Assert.True(vm.AutoRefreshEnabled);
        Assert.Equal(2, vm.NotificationEnabledIndex); // 服务健康通知默认关闭
        Assert.False(vm.ShowProbeConsumesQuotaHint);
    }

    [Fact]
    public void NewAiAccount_KeepsV080Defaults()
    {
        var vm = new AccountEditorViewModel(
            new FakeAccountManager(),
            Context(new DeepSeekBalanceProvider(FakeHttpRequestService.Returning("{}")).Info));

        Assert.True(vm.AutoRefreshEnabled);
        Assert.Equal(30, vm.RefreshIntervalMinutes);
        Assert.Equal(0, vm.NotificationEnabledIndex);
        Assert.False(vm.ShowProbeConsumesQuotaHint);
    }

    [Fact]
    public void EditingMapAccount_PreservesStoredMonitoring()
    {
        var context = new AccountEditorContext
        {
            AccountId = "acct-amap",
            Providers = new[] { Amap.Info },
            InitialProviderId = "amap",
            InitialDisplayName = "AMap",
            HasStoredCredential = true,
            CredentialSlots = new Dictionary<string, bool> { [CredentialSlots.Primary] = true },
            InitialMonitoring = new MonitoringSettings
            {
                AutoRefreshEnabled = true,
                RefreshIntervalMinutes = 180,
            },
            CurrentMetrics = Array.Empty<BalanceMetric>(),
        };

        var vm = new AccountEditorViewModel(new FakeAccountManager(), context);

        Assert.True(vm.AutoRefreshEnabled);
        Assert.Equal(180, vm.RefreshIntervalMinutes);
    }

    [Fact]
    public void OgcConditionalSlots_FollowAuthMode()
    {
        var vm = new AccountEditorViewModel(new FakeAccountManager(), Context(Ogc.Info));
        var authMode = vm.ConfigFieldItems.Single(f => f.FieldId == OgcServiceProvider.AuthModeField);
        var username = vm.CredentialSlotItems.Single(s => s.SlotId == CredentialSlots.Username);
        var password = vm.CredentialSlotItems.Single(s => s.SlotId == CredentialSlots.Password);

        // 默认 none：不显示 Basic 槽位。
        Assert.False(username.IsVisible);
        Assert.False(password.IsVisible);

        authMode.Value = "basic";

        Assert.True(username.IsVisible);
        Assert.True(password.IsVisible);
        Assert.True(vm.CredentialSlotItems.Single(s => s.SlotId == CredentialSlots.BearerToken).IsVisible == false);
    }

    [Fact]
    public void OgcBasic_RequiresUsernameAndPassword_NoPrimaryNeeded()
    {
        var vm = new AccountEditorViewModel(new FakeAccountManager(), Context(Ogc.Info));
        vm.DisplayName = "GeoServer WMS";
        vm.ConfigFieldItems.Single(f => f.FieldId == OgcServiceProvider.AuthModeField).Value = "basic";
        vm.ConfigFieldItems.Single(f => f.FieldId == OgcServiceProvider.ServiceTypeField).Value = "wms";
        vm.ConfigFieldItems.Single(f => f.FieldId == OgcServiceProvider.CapabilitiesUrlField).Value =
            "https://gis.example.test/geoserver/wms?request=GetCapabilities";

        Assert.False(vm.CanSave);
        Assert.False(vm.CanTest);

        vm.SetCredentialSlotValue(CredentialSlots.Username, "alice");
        vm.SetCredentialSlotValue(CredentialSlots.Password, "secret");

        Assert.True(vm.CanSave);
        Assert.True(vm.CanTest);
        Assert.True(vm.TryBuildResult(out var result));
        Assert.Equal("alice", result!.CredentialSlots[CredentialSlots.Username]);
        Assert.Equal("secret", result.CredentialSlots[CredentialSlots.Password]);
        Assert.DoesNotContain(CredentialSlots.Primary, result.CredentialSlots.Keys);
    }

    [Fact]
    public void MapProvider_ExposesSecretSlot()
    {
        var vm = new AccountEditorViewModel(new FakeAccountManager(), Context(Amap.Info));

        Assert.Contains(vm.CredentialSlotItems, s => s.SlotId == CredentialSlots.Primary && s.IsRequired);
        Assert.Contains(vm.CredentialSlotItems, s => s.SlotId == CredentialSlots.Secret && !s.IsRequired);
    }
}
