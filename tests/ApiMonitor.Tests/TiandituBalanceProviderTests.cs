using System.Net;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 天地图地名搜索 V2.0 探测测试（全部使用 Mock）。
/// 返回信息码表以官方文档（lbs.tianditu.gov.cn/server/search2.html 2.1 节）为契约。
/// 官方未公开 Token 无效等状态码：未知码映射 ProviderError，不猜测语义。
/// </summary>
public sealed class TiandituBalanceProviderTests
{
    private const string Token = "tianditu-test-token-not-real";

    private static ApiAccount TestAccount() =>
        new()
        {
            AccountId = "acct-tianditu",
            ProviderId = "tianditu",
            DisplayName = "Tianditu",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static async Task<BalanceQueryResult> QueryAsync(FakeHttpRequestService http, string json)
    {
        var provider = new TiandituBalanceProvider(http);
        return await provider.QueryBalanceAsync(
            TestAccount(),
            new Dictionary<string, string> { [CredentialSlots.Primary] = Token },
            CancellationToken.None);
    }

    private static BalanceMetric ServiceMetric(BalanceSnapshot snapshot) =>
        snapshot.Metrics.First(m => m.MetricId == "tianditu:service.availability");

    [Fact]
    public async Task Infocode1000_ReportsHealthy()
    {
        const string json = """{ "status": { "infocode": 1000, "cndesc": "服务正常" }, "count": 1 }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.True(result.IsSuccess);
        Assert.True(result.Snapshot!.IsAvailable);
        Assert.Equal("Healthy", ServiceMetric(result.Snapshot).StatusValue);
    }

    [Theory]
    [InlineData(2001, "InvalidResponse")]
    [InlineData(2002, "InvalidResponse")]
    [InlineData(2003, "ConfigurationMissing")]
    [InlineData(2004, "InvalidResponse")]
    [InlineData(2005, "InvalidResponse")]
    [InlineData(2006, "InvalidResponse")]
    [InlineData(2007, "InvalidResponse")]
    [InlineData(3000, "ProviderError")]
    [InlineData(7000, "ProviderError")]
    public async Task Infocodes_MapToStatus(int infocode, string expected)
    {
        string json = $$"""{ "status": { "infocode": {{infocode}}, "cndesc": "x" } }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, ServiceMetric(result.Snapshot!).StatusValue);
    }

    [Fact]
    public async Task Infocode3001_NoData_IsHealthy()
    {
        const string json = """{ "status": { "infocode": 3001, "cndesc": "没有找到数据" } }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.True(result.IsSuccess);
        Assert.True(result.Snapshot!.IsAvailable);
        Assert.Equal("Healthy", ServiceMetric(result.Snapshot).StatusValue);
    }

    [Fact]
    public async Task StatusObjectMissing_ReturnsInvalidResponse()
    {
        const string json = """{ "count": 0 }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public async Task InvalidJson_ReturnsInvalidJson()
    {
        var result = await QueryAsync(FakeHttpRequestService.Returning("""{ "status": { """), string.Empty);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidJson, result.Error!.Kind);
    }

    [Fact]
    public async Task Probe_EncodesPostJsonCorrectly()
    {
        var http = FakeHttpRequestService.Returning(
            """{ "status": { "infocode": 1000, "cndesc": "ok" } }""");

        await QueryAsync(http, string.Empty);

        var url = Assert.Single(http.RequestUrls);
        Assert.StartsWith("https://api.tianditu.gov.cn/v2/search?", url);
        Assert.Contains("type=query", url);
        Assert.Contains($"tk={Token}", url);
        Assert.Contains("postStr=", url);
        // postStr 必须是 URL 编码后的 JSON（不应包含裸双引号/花括号）。
        Assert.DoesNotContain("{\"", url);
        Assert.Contains("keyWord", url);
    }

    [Fact]
    public async Task HttpErrors_AreClassified()
    {
        var result = await QueryAsync(
            FakeHttpRequestService.Returning("{}", HttpStatusCode.Unauthorized),
            "{}");

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.CredentialInvalid, result.Error!.Kind);
    }
}
