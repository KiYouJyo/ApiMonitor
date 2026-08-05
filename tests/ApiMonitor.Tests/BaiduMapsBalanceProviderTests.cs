using System.Net;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 百度地图开放平台探测测试（全部使用 Mock）。
/// 状态码映射以官方附录（lbs.baidu.com/faq/api?title=webapi/appendix）为契约。
/// </summary>
public sealed class BaiduMapsBalanceProviderTests
{
    private const string Ak = "baidu-test-ak-not-real";

    private static ApiAccount TestAccount() =>
        new()
        {
            AccountId = "acct-baidu",
            ProviderId = "baidu-maps",
            DisplayName = "Baidu",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static async Task<BalanceQueryResult> QueryAsync(FakeHttpRequestService http, string json)
    {
        var provider = new BaiduMapsBalanceProvider(http);
        return await provider.QueryBalanceAsync(
            TestAccount(),
            new Dictionary<string, string> { [CredentialSlots.Primary] = Ak },
            CancellationToken.None);
    }

    private static BalanceMetric ServiceMetric(BalanceSnapshot snapshot) =>
        snapshot.Metrics.First(m => m.MetricId == "baidu-maps:service.availability");

    [Fact]
    public async Task StatusZero_ReportsHealthy()
    {
        const string json = """{ "status": 0, "result": { "location": { "lng": 116.3, "lat": 40.0 } } }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.True(result.IsSuccess);
        Assert.True(result.Snapshot!.IsAvailable);
        Assert.Equal("Healthy", ServiceMetric(result.Snapshot).StatusValue);
    }

    [Theory]
    [InlineData(1, GeospatialStatus.ProviderError)]
    [InlineData(2, GeospatialStatus.InvalidResponse)]
    [InlineData(3, GeospatialStatus.PermissionDenied)]
    [InlineData(4, GeospatialStatus.QuotaExceeded)]
    [InlineData(5, GeospatialStatus.CredentialInvalid)]
    [InlineData(101, GeospatialStatus.ConfigurationMissing)]
    [InlineData(102, GeospatialStatus.IpWhitelistDenied)]
    [InlineData(240, GeospatialStatus.ServiceNotEnabled)]
    [InlineData(201, GeospatialStatus.PermissionDenied)]
    [InlineData(302, GeospatialStatus.QuotaExceeded)]
    [InlineData(401, GeospatialStatus.RateLimited)]
    [InlineData(9999, GeospatialStatus.ProviderError)]
    public async Task StatusCodes_MapToStatus(int status, GeospatialStatus expected)
    {
        string json = $$"""{ "status": {{status}}, "message": "x" }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.True(result.IsSuccess);
        Assert.False(result.Snapshot!.IsAvailable);
        Assert.Equal(expected.ToString(), ServiceMetric(result.Snapshot).StatusValue);
    }

    [Fact]
    public async Task StructureWithoutStatus_ReturnsInvalidResponse()
    {
        const string json = """{ "result": {} }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public async Task InvalidJson_ReturnsInvalidJson()
    {
        const string json = """{ "status": 0, """;

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidJson, result.Error!.Kind);
    }

    [Fact]
    public async Task HttpStatusErrors_AreClassified()
    {
        var result = await QueryAsync(
            FakeHttpRequestService.Returning("{}", HttpStatusCode.Unauthorized),
            "{}");

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.CredentialInvalid, result.Error!.Kind);
    }

    [Fact]
    public async Task Request_UsesOfficialHostWithAkInQuery()
    {
        var http = FakeHttpRequestService.Returning("""{ "status": 0, "result": {} }""");

        await QueryAsync(http, string.Empty);

        var url = Assert.Single(http.RequestUrls);
        Assert.StartsWith("https://api.map.baidu.com/geocoding/v3/", url);
        Assert.Contains($"ak={Ak}", url);
    }
}
