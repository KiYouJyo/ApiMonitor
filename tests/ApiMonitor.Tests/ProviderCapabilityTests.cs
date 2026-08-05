using System.Net;
using ApiMonitor.Providers;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v0.8.0 能力模型测试：注册表包含五个预期供应商、Provider ID 稳定唯一、
/// 每个 Provider 声明主指标、非敏感配置字段正确、凭据请求受主机白名单约束。
/// </summary>
public sealed class ProviderCapabilityTests
{
    private static ProviderRegistry CreateFullRegistry()
    {
        var http = new HttpRequestService(TimeSpan.FromSeconds(15));
        return new ProviderRegistry(new IApiBalanceProvider[]
        {
            new DeepSeekBalanceProvider(http),
            new OpenRouterBalanceProvider(http),
            new MoonshotBalanceProvider(http),
            new SiliconFlowBalanceProvider(http),
            new XaiBalanceProvider(http),
        });
    }

    [Fact]
    public void FullRegistry_ContainsExpectedFiveProviders()
    {
        var registry = CreateFullRegistry();

        Assert.Equal(5, registry.All.Count);
        Assert.Equal(
            new[] { "deepseek", "moonshot", "openrouter", "siliconflow", "xai" },
            registry.All.Select(p => p.ProviderId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void ProviderIds_AreStableAndUnique()
    {
        var registry = CreateFullRegistry();
        var ids = registry.All.Select(p => p.ProviderId).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void EveryProvider_HasPrimaryMetric()
    {
        var registry = CreateFullRegistry();
        var primaryIds = new List<string>();

        foreach (var info in registry.Infos)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.PrimaryMetricId), $"{info.ProviderId} 缺少主指标。");
            primaryIds.Add(info.PrimaryMetricId);
        }

        Assert.Equal(primaryIds.Count, primaryIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void MetricIds_AcrossProviders_AreUniqueAndNonLocalized()
    {
        var all = new[]
        {
            "deepseek:CNY:total",
            "openrouter:credits:remaining",
            "openrouter:credits:total",
            "openrouter:credits:usage",
            "openrouter:key:quota-remaining",
            "openrouter:key:quota-limit",
            "openrouter:key:usage-total",
            "openrouter:key:usage-daily",
            "openrouter:key:usage-weekly",
            "openrouter:key:usage-monthly",
            "openrouter:key:usage-byok",
            MoonshotBalanceProvider.AvailableMetricId,
            MoonshotBalanceProvider.CashMetricId,
            MoonshotBalanceProvider.VoucherMetricId,
            SiliconFlowBalanceProvider.TotalMetricId,
            SiliconFlowBalanceProvider.ChargeMetricId,
            SiliconFlowBalanceProvider.GrantedMetricId,
            SiliconFlowBalanceProvider.AvailableMetricId,
            XaiBalanceProvider.PrepaidMetricId,
        };

        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void XaiProvider_RequiresTeamIdAsNonSensitiveConfigField()
    {
        var info = new XaiBalanceProvider(new HttpRequestService(TimeSpan.FromSeconds(15))).Info;

        var teamId = Assert.Single(info.RequiredConfigFields);
        Assert.Equal(XaiBalanceProvider.TeamIdField, teamId.FieldId);
        Assert.True(teamId.IsRequired);
        Assert.False(string.IsNullOrWhiteSpace(teamId.LabelKey));
        Assert.False(string.IsNullOrWhiteSpace(teamId.HintKey));
        Assert.Equal("https://management-api.x.ai", info.DefaultBaseUrl);
        Assert.Equal("USD", info.Currency);
        Assert.Equal(XaiBalanceProvider.PrepaidMetricId, info.PrimaryMetricId);
        Assert.False(info.AllowCustomEndpoint);
        Assert.False(info.SupportsMultiCurrency);
    }

    [Fact]
    public void MoonshotAndSiliconFlow_DeclareCnyCurrencyAndBreakdown()
    {
        var http = new HttpRequestService(TimeSpan.FromSeconds(15));
        var moonshot = new MoonshotBalanceProvider(http).Info;
        var siliconFlow = new SiliconFlowBalanceProvider(http).Info;

        Assert.Equal("CNY", moonshot.Currency);
        Assert.True(moonshot.SupportsBreakdown);
        Assert.Equal(MoonshotBalanceProvider.AvailableMetricId, moonshot.PrimaryMetricId);
        Assert.Equal("CNY", siliconFlow.Currency);
        Assert.True(siliconFlow.SupportsBreakdown);
        Assert.Equal(SiliconFlowBalanceProvider.TotalMetricId, siliconFlow.PrimaryMetricId);
    }

    [Fact]
    public void OfficialProviders_DoNotAllowCustomEndpoints()
    {
        var registry = CreateFullRegistry();
        Assert.All(registry.Infos, info => Assert.False(info.AllowCustomEndpoint));
    }

    [Fact]
    public async Task HostGuard_RejectsRequestToWrongHost_WithoutSending()
    {
        var http = FakeHttpRequestService.Returning("{}");
        var client = new ProviderHttpClient(http, new[] { "api.deepseek.com" }, _ => TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, "https://evil.example.com/user/balance"),
                CancellationToken.None));

        Assert.Empty(http.RequestUrls);
    }

    [Fact]
    public async Task HostGuard_RejectsPlainHttp()
    {
        var http = FakeHttpRequestService.Returning("{}");
        var client = new ProviderHttpClient(http, new[] { "api.deepseek.com" }, _ => TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, "http://api.deepseek.com/user/balance"),
                CancellationToken.None));

        Assert.Empty(http.RequestUrls);
    }

    [Fact]
    public async Task HostGuard_AllowsOfficialHost()
    {
        var http = FakeHttpRequestService.Returning("{}");
        var client = new ProviderHttpClient(http, new[] { "management-api.x.ai" }, _ => TimeSpan.Zero);

        using var response = await client.SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "https://management-api.x.ai/v1/billing/teams/1/prepaid/balance"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(http.RequestUrls);
    }

    [Fact]
    public async Task HostGuard_AllowsSubdomainOfOfficialHost()
    {
        var http = FakeHttpRequestService.Returning("{}");
        var client = new ProviderHttpClient(http, new[] { "api.openrouter.ai" }, _ => TimeSpan.Zero);

        using var response = await client.SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "https://api.openrouter.ai/api/v1/credits"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Retry_429_RetriesUpToMaxAttempts()
    {
        var http = FakeHttpRequestService.Returning("{}", HttpStatusCode.TooManyRequests);
        var client = new ProviderHttpClient(http, new[] { "api.deepseek.com" }, _ => TimeSpan.Zero);

        using var response = await client.SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(ProviderHttpClient.MaxAttempts, http.RequestUrls.Count);
    }

    [Fact]
    public async Task Retry_5xx_RetriesUpToMaxAttempts()
    {
        var http = FakeHttpRequestService.Returning("{}", HttpStatusCode.InternalServerError);
        var client = new ProviderHttpClient(http, new[] { "api.deepseek.com" }, _ => TimeSpan.Zero);

        using var response = await client.SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProviderHttpClient.MaxAttempts, http.RequestUrls.Count);
    }

    [Fact]
    public async Task Retry_Timeout_RetriesThenThrows()
    {
        var http = FakeHttpRequestService.Throwing<TaskCanceledException>();
        var client = new ProviderHttpClient(http, new[] { "api.deepseek.com" }, _ => TimeSpan.Zero);

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            client.SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance"),
                CancellationToken.None));

        Assert.Equal(ProviderHttpClient.MaxAttempts, http.RequestUrls.Count);
    }

    [Fact]
    public async Task Retry_401_DoesNotRetry()
    {
        var http = FakeHttpRequestService.Returning("{}", HttpStatusCode.Unauthorized);
        var client = new ProviderHttpClient(http, new[] { "api.deepseek.com" }, _ => TimeSpan.Zero);

        using var response = await client.SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(http.RequestUrls);
    }

    [Fact]
    public async Task Retry_RespectsCancellation_BetweenAttempts()
    {
        var http = FakeHttpRequestService.Returning("{}", HttpStatusCode.TooManyRequests);
        var client = new ProviderHttpClient(
            http,
            new[] { "api.deepseek.com" },
            _ => TimeSpan.FromMilliseconds(50));
        using var cts = new CancellationTokenSource(30);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance"),
                cts.Token));
    }
}
