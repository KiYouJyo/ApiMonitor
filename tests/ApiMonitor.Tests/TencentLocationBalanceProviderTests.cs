using System.Net;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 腾讯位置服务探测测试（全部使用 Mock）。
/// 状态码映射以官方文档（lbs.qq.com/service/webService/webServiceGuide/status）为契约。
/// </summary>
public sealed class TencentLocationBalanceProviderTests
{
    private const string Key = "tencent-test-key-not-real";

    private static ApiAccount TestAccount() =>
        new()
        {
            AccountId = "acct-tencent",
            ProviderId = "tencent-location",
            DisplayName = "Tencent",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static async Task<BalanceQueryResult> QueryAsync(FakeHttpRequestService http, string json)
    {
        var provider = new TencentLocationBalanceProvider(http);
        return await provider.QueryBalanceAsync(
            TestAccount(),
            new Dictionary<string, string> { [CredentialSlots.Primary] = Key },
            CancellationToken.None);
    }

    private static BalanceMetric ServiceMetric(BalanceSnapshot snapshot) =>
        snapshot.Metrics.First(m => m.MetricId == "tencent-location:service.availability");

    [Fact]
    public async Task StatusZero_ReportsHealthy()
    {
        const string json = """{ "status": 0, "message": "query ok", "result": [[], []] }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.True(result.IsSuccess);
        Assert.True(result.Snapshot!.IsAvailable);
        Assert.Equal("Healthy", ServiceMetric(result.Snapshot).StatusValue);
    }

    [Theory]
    [InlineData(110, GeospatialStatus.PermissionDenied)]
    [InlineData(111, GeospatialStatus.SignatureInvalid)]
    [InlineData(112, GeospatialStatus.IpWhitelistDenied)]
    [InlineData(113, GeospatialStatus.PermissionDenied)]
    [InlineData(120, GeospatialStatus.RateLimited)]
    [InlineData(121, GeospatialStatus.QuotaExceeded)]
    [InlineData(190, GeospatialStatus.CredentialInvalid)]
    [InlineData(199, GeospatialStatus.ServiceNotEnabled)]
    [InlineData(301, GeospatialStatus.ConfigurationMissing)]
    [InlineData(311, GeospatialStatus.KeyTypeMismatch)]
    [InlineData(500, GeospatialStatus.ProviderError)]
    [InlineData(700, GeospatialStatus.ProviderError)]
    public async Task StatusCodes_MapToStatus(int status, GeospatialStatus expected)
    {
        string json = $$"""{ "status": {{status}}, "message": "x" }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.True(result.IsSuccess);
        Assert.False(result.Snapshot!.IsAvailable);
        Assert.Equal(expected.ToString(), ServiceMetric(result.Snapshot).StatusValue);
    }

    [Fact]
    public async Task ParamError_IsInvalidResponse()
    {
        const string json = """{ "status": 310, "message": "wrong param" }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.True(result.IsSuccess);
        Assert.Equal("InvalidResponse", ServiceMetric(result.Snapshot!).StatusValue);
    }

    [Fact]
    public async Task InvalidJson_ReturnsInvalidJson()
    {
        var result = await QueryAsync(FakeHttpRequestService.Returning("""{ "status": 0, """), string.Empty);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidJson, result.Error!.Kind);
    }

    [Fact]
    public async Task HttpStatusErrors_AreClassified()
    {
        var result = await QueryAsync(
            FakeHttpRequestService.Returning("{}", HttpStatusCode.Forbidden),
            "{}");

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.PermissionDenied, result.Error!.Kind);
    }

    [Fact]
    public async Task Probe_UsesDistrictListEndpoint()
    {
        var http = FakeHttpRequestService.Returning("""{ "status": 0, "result": [] }""");

        await QueryAsync(http, string.Empty);

        var url = Assert.Single(http.RequestUrls);
        Assert.StartsWith("https://apis.map.qq.com/ws/district/v1/list?", url);
        Assert.Contains($"key={Key}", url);
    }
}
