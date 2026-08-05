using System.Net;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 高德开放平台探测测试（全部使用 Mock，不调用真实接口）。
/// 状态码映射以官方文档（lbs.amap.com/api/webservice/guide/tools/info）为契约。
/// </summary>
public sealed class AmapBalanceProviderTests
{
    private const string ApiKey = "amap-test-key-not-real";

    private static ApiAccount TestAccount() =>
        new()
        {
            AccountId = "acct-amap",
            ProviderId = "amap",
            DisplayName = "AMap",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static AmapBalanceProvider CreateProvider(FakeHttpRequestService http) =>
        new(http);

    private static Task<BalanceQueryResult> QueryAsync(FakeHttpRequestService http, string json) =>
        CreateProvider(http).QueryBalanceAsync(
            TestAccount(),
            new Dictionary<string, string> { [CredentialSlots.Primary] = ApiKey },
            CancellationToken.None);

    private static BalanceMetric ServiceMetric(BalanceSnapshot snapshot) =>
        snapshot.Metrics.First(m => m.MetricId == "amap:service.availability");

    [Fact]
    public async Task NormalResponse_ReportsHealthy()
    {
        const string json = """{ "status": "1", "info": "OK", "infocode": "10000", "geocodes": [] }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.True(result.IsSuccess);
        Assert.True(result.Snapshot!.IsAvailable);
        Assert.Equal("Healthy", ServiceMetric(result.Snapshot).StatusValue);
        Assert.NotNull(result.Snapshot.Metrics.FirstOrDefault(m =>
            m.MetricId == "amap:service.latency.ms" && m.IntegerValue is not null));
    }

    [Theory]
    [InlineData("10001", GeospatialStatus.CredentialInvalid)]
    [InlineData("10002", GeospatialStatus.ServiceNotEnabled)]
    [InlineData("10003", GeospatialStatus.QuotaExceeded)]
    [InlineData("10004", GeospatialStatus.RateLimited)]
    [InlineData("10005", GeospatialStatus.IpWhitelistDenied)]
    [InlineData("10007", GeospatialStatus.SignatureInvalid)]
    [InlineData("10009", GeospatialStatus.KeyTypeMismatch)]
    [InlineData("99999", GeospatialStatus.ProviderError)]
    public async Task ErrorInfocodes_MapToStatus(string infocode, GeospatialStatus expected)
    {
        string json = $$"""{ "status": "0", "info": "FAIL", "infocode": "{{infocode}}" }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.True(result.IsSuccess);
        Assert.False(result.Snapshot!.IsAvailable);
        Assert.Equal(expected.ToString(), ServiceMetric(result.Snapshot).StatusValue);
    }

    [Fact]
    public async Task StatusFieldMissing_ReturnsInvalidResponse()
    {
        const string json = """{ "infocode": "10000" }""";

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public async Task InvalidJson_ReturnsInvalidJson()
    {
        const string json = """{ "status": "1", """;

        var result = await QueryAsync(FakeHttpRequestService.Returning(json), json);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidJson, result.Error!.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BalanceErrorKind.CredentialInvalid)]
    [InlineData(HttpStatusCode.Forbidden, BalanceErrorKind.PermissionDenied)]
    [InlineData(HttpStatusCode.NotFound, BalanceErrorKind.NotFound)]
    [InlineData((HttpStatusCode)429, BalanceErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, BalanceErrorKind.ServerError)]
    [InlineData(HttpStatusCode.Redirect, BalanceErrorKind.RedirectBlocked)]
    public async Task HttpErrors_MapToClassifiedKinds(HttpStatusCode status, BalanceErrorKind expected)
    {
        var result = await QueryAsync(FakeHttpRequestService.Returning("{}", status), "{}");

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Error!.Kind);
        Assert.Equal((int)status, result.Error.HttpStatusCode);
    }

    [Fact]
    public async Task Request_OnlyGoesToOfficialHost_WithKeyInQuery()
    {
        var http = FakeHttpRequestService.Returning(
            """{ "status": "1", "info": "OK", "infocode": "10000", "geocodes": [] }""");

        await QueryAsync(http, string.Empty);

        var url = Assert.Single(http.RequestUrls);
        Assert.StartsWith("https://restapi.amap.com/v3/geocode/geo?", url);
        Assert.Contains("address=", url);
        Assert.Contains($"key={ApiKey}", url);
    }

    [Fact]
    public async Task ProviderInfo_DeclaresGeospatialCategoryAndQuotaConsumption()
    {
        var info = CreateProvider(FakeHttpRequestService.Returning("{}")).Info;

        Assert.Equal(ProviderCategory.Geospatial, info.EffectiveCategory);
        Assert.True(info.EffectiveProbeConsumesQuota);
        Assert.False(info.SupportsAccountBalance);
    }
}
