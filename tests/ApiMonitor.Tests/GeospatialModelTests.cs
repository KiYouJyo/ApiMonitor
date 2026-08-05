using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v0.9.0 通用模型测试：注册表、分类、值类型、JSON 向后兼容与汇总隔离。
/// </summary>
public sealed class GeospatialModelTests
{
    private static ProviderRegistry CreateRegistry() =>
        new(new IApiBalanceProvider[]
        {
            new DeepSeekBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new OpenRouterBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new MoonshotBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new SiliconFlowBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new XaiBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new AmapBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new BaiduMapsBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new TencentLocationBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new TiandituBalanceProvider(FakeHttpRequestService.Returning("{}")),
            new SuperMapIServerProvider(FakeHttpRequestService.Returning("{}")),
            new OgcServiceProvider(FakeHttpRequestService.Returning("{}")),
        });

    [Fact]
    public void Registry_ContainsAllNewProviders_WithUniqueIds()
    {
        var registry = CreateRegistry();

        foreach (string id in new[]
        {
            "amap",
            "baidu-maps",
            "tencent-location",
            "tianditu",
            "supermap-iserver",
            "ogc-service",
        })
        {
            Assert.NotNull(registry.GetById(id));
        }

        Assert.Equal(registry.All.Count, registry.All.Select(p => p.ProviderId).Distinct().Count());
    }

    [Fact]
    public void Categories_AreDeclaredCorrectly()
    {
        var registry = CreateRegistry();

        Assert.All(new[] { "amap", "baidu-maps", "tencent-location", "tianditu" }, id =>
            Assert.Equal(ProviderCategory.Geospatial, registry.GetById(id)!.Info.EffectiveCategory));
        Assert.All(new[] { "supermap-iserver", "ogc-service" }, id =>
            Assert.Equal(ProviderCategory.GisServer, registry.GetById(id)!.Info.EffectiveCategory));
        Assert.All(new[] { "deepseek", "openrouter", "moonshot", "siliconflow", "xai" }, id =>
            Assert.Equal(ProviderCategory.ArtificialIntelligence, registry.GetById(id)!.Info.EffectiveCategory));
    }

    [Fact]
    public void OldJsonMetrics_DeserializeWithDecimalValueKind()
    {
        const string json = """
            {
              "metricId": "deepseek:CNY:total",
              "displayName": "CNY 总余额",
              "unit": "CNY",
              "kind": "MonetaryBalance",
              "availableAmount": 12.5,
              "isThresholdSupported": true
            }
            """;

        var entry = System.Text.Json.JsonSerializer.Deserialize<BalanceMetricFileEntry>(
            json,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

        Assert.NotNull(entry);
        Assert.Equal(nameof(MetricValueKind.Decimal), entry!.ValueKind);
        Assert.Null(entry.DetailedKind);
        Assert.Null(entry.IntegerValue);
        Assert.Null(entry.StatusValue);

        var metric = StorageMapper.ToBalanceMetric(entry);
        Assert.Equal(MetricValueKind.Decimal, metric.ValueKind);
        Assert.Equal(12.5m, metric.AvailableAmount);
    }

    [Fact]
    public void GeospatialMetric_RoundTripsThroughStorageMapper()
    {
        var metric = GeospatialMetricFactory.BuildMapMetricSet(
            "amap",
            GeospatialStatus.QuotaExceeded,
            123L)[0];
        var entry = StorageMapper.ToBalanceMetricFileEntry(metric);

        var restored = StorageMapper.ToBalanceMetric(entry);

        Assert.Equal("amap:service.availability", restored.MetricId);
        Assert.Equal(MetricValueKind.Status, restored.ValueKind);
        Assert.Equal(MetricKind.ServiceAvailability, restored.DetailedKind);
        Assert.Equal("QuotaExceeded", restored.StatusValue);
        Assert.Null(restored.AvailableAmount);
    }

    [Fact]
    public void UnknownQuota_StaysNull_NeverZero()
    {
        var metrics = GeospatialMetricFactory.BuildMapMetricSet(
            "amap",
            GeospatialStatus.Healthy,
            10L);

        Assert.DoesNotContain(metrics, m => m.MetricId.EndsWith("quota.remaining", StringComparison.Ordinal));
        Assert.DoesNotContain(metrics, m => m.AvailableAmount == 0m);
    }

    [Fact]
    public void StatusMetric_DoesNotTriggerLowBalanceThreshold()
    {
        var metric = GeospatialMetricFactory.ServiceAvailability("amap", GeospatialStatus.QuotaExceeded);
        var rule = new BalanceThresholdRule
        {
            MetricId = metric.MetricId,
            DisplayName = metric.DisplayName,
            Unit = metric.Unit,
            IsEnabled = true,
            ThresholdAmount = 0m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        // 状态指标不支持阈值：评估器不得将其判定为“低于阈值”。
        Assert.False(metric.IsThresholdSupported);
        Assert.Equal(ThresholdStatus.Disabled, ThresholdEvaluator.Evaluate(metric, rule));
    }

    [Fact]
    public async Task BalanceSummary_ExcludesGeospatialAccounts()
    {
        // 通过主界面 ViewModel 验证：地理账户不进入“余额账户/低余额”统计。
        var manager = new FakeAccountManager();
        manager.ProviderList.Add(new AmapBalanceProvider(FakeHttpRequestService.Returning("{}")).Info);
        manager.Accounts.Add(new ApiAccount
        {
            AccountId = "acct-amap",
            ProviderId = "amap",
            DisplayName = "AMap",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        var viewModel = new ViewModels.MainViewModel(
            manager,
            new FakeDialogService(),
            new AppLog(System.IO.Path.GetTempPath()),
            new FakeClipboardService(),
            new FakeUiThreadInvoker());
        await viewModel.InitializeAsync();

        Assert.Equal(0, viewModel.BalanceAccountCount);
        Assert.Equal(1, viewModel.TotalAccountCount);
    }
}
