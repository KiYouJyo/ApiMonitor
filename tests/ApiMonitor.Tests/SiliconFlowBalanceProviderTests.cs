using System.Net;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class SiliconFlowBalanceProviderTests
{
    private const string ApiKey = "sk-test-only-not-real";

    private static ApiAccount TestAccount() =>
        new()
        {
            AccountId = "acct-sf-1",
            ProviderId = "siliconflow",
            DisplayName = "SiliconFlow",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static SiliconFlowBalanceProvider CreateProvider(FakeHttpRequestService http) =>
        new(http);

    private const string OfficialJson = """
        {
          "code": 20000,
          "message": "OK",
          "status": true,
          "data": {
            "id": "userid",
            "name": "username",
            "image": "https://example.invalid/avatar.png",
            "email": "user@example.invalid",
            "isAdmin": false,
            "balance": "0.88",
            "status": "normal",
            "introduction": "",
            "role": "",
            "chargeBalance": "88.00",
            "totalBalance": "88.88"
          }
        }
        """;

    [Fact]
    public async Task OfficialResponse_ParsesTotalWithoutDoubleCounting()
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning(OfficialJson))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snapshot = result.Snapshot!;
        Assert.True(snapshot.IsAvailable);
        Assert.Equal("acct-sf-1", snapshot.AccountId);

        var total = Assert.Single(snapshot.Metrics, m => m.MetricId == "siliconflow:balance.total.cny");
        // 主指标直接读取官方 totalBalance，绝不再次把 balance + chargeBalance 相加。
        Assert.Equal(88.88m, total.AvailableAmount);
        Assert.Equal(88.88m, total.TotalAmount);
        Assert.Equal(88.00m, total.ToppedUpAmount);
        Assert.Null(total.GrantedAmount);
        Assert.True(total.IsThresholdSupported);

        Assert.Equal(0.88m, Assert.Single(snapshot.Metrics, m => m.MetricId == "siliconflow:balance.available.cny").AvailableAmount);
        Assert.Equal(88.00m, Assert.Single(snapshot.Metrics, m => m.MetricId == "siliconflow:balance.charge.cny").ToppedUpAmount);
    }

    [Fact]
    public async Task TotalOnly_StillParsesAndSecondaryFieldsAreNull()
    {
        const string json = """
            {
              "code": 20000,
              "status": true,
              "data": { "id": "u1", "totalBalance": 12.34 }
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var total = result.Snapshot!.Metrics.Single();
        Assert.Equal(12.34m, total.AvailableAmount);
        Assert.Null(total.ToppedUpAmount);
        Assert.Null(total.GrantedAmount);
    }

    [Fact]
    public async Task GrantedBalance_WhenPresent_MapsToGrantedMetric()
    {
        const string json = """
            {
              "code": 20000,
              "status": true,
              "data": { "totalBalance": "100.00", "chargeBalance": "70.00", "grantedBalance": "30.00" }
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var granted = Assert.Single(result.Snapshot!.Metrics, m => m.MetricId == "siliconflow:balance.granted.cny");
        Assert.Equal(30.00m, granted.GrantedAmount);
        // 主指标只读 totalBalance，不允许手工相加。
        Assert.Equal(100.00m, Assert.Single(result.Snapshot.Metrics, m => m.MetricId == "siliconflow:balance.total.cny").AvailableAmount);
    }

    [Fact]
    public async Task UserProfileFields_AreIgnoredAndNeverExposed()
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning(OfficialJson))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var text = string.Join(" ", result.Snapshot!.Metrics.Select(m => $"{m.DisplayName}|{m.Unit}"));
        Assert.DoesNotContain("username", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user@example.invalid", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("avatar", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingTotalBalance_ReturnsUnsupportedFormat()
    {
        const string json = """
            {
              "code": 20000,
              "status": true,
              "data": { "name": "user", "email": "user@example.invalid", "balance": "5.00" }
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
        // 错误信息绝不包含用户资料或密钥。
        Assert.DoesNotContain("user@example.invalid", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiKey, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenamedOrMissingFields_ReturnUnsupportedFormat()
    {
        const string json = """
            {
              "code": 20000,
              "status": true,
              "data": { "total_balance": "88.88" }
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public async Task NonSuccessCode_ReturnsUnsupportedFormat()
    {
        const string json = """
            {
              "code": 40100,
              "status": false,
              "message": "invalid key",
              "data": { "totalBalance": "88.88" }
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.DoesNotContain("invalid key", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountStatusNotNormal_IsNotAvailable()
    {
        const string json = """
            {
              "code": 20000,
              "status": true,
              "data": { "totalBalance": "10.00", "status": "suspended" }
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Snapshot!.IsAvailable);
    }

    [Theory]
    [InlineData("0.88", 0.88)]
    [InlineData("12.345", 12.345)]
    [InlineData("-3.00", -3.00)]
    public async Task NumericStrings_ParseToDecimal(string value, decimal expected)
    {
        string json =
            $$"""
            {
              "code": 20000,
              "status": true,
              "data": { "totalBalance": "{{value}}" }
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Snapshot!.Metrics[0].AvailableAmount);
    }

    [Fact]
    public async Task InvalidJson_ReturnsInvalidJson()
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning("""{ "code": 20000, "data": { "totalBalance": """))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidJson, result.Error!.Kind);
    }

    [Fact]
    public async Task EmptyBody_ReturnsEmptyContentError()
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning(string.Empty))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.EmptyContent, result.Error!.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BalanceErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, BalanceErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, BalanceErrorKind.AccountNotFound)]
    [InlineData((HttpStatusCode)429, BalanceErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, BalanceErrorKind.ServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, BalanceErrorKind.ServerError)]
    public async Task HttpErrorStatus_ReturnsClassifiedError(HttpStatusCode status, BalanceErrorKind expectedKind)
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning("{}", status))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedKind, result.Error!.Kind);
    }

    [Fact]
    public async Task HttpErrorMessages_NeverContainApiKey()
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning("{}", HttpStatusCode.Unauthorized))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(ApiKey, result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApiKeyGoesIntoAuthorizationHeader_NotIntoUrl()
    {
        var http = FakeHttpRequestService.Returning(OfficialJson);

        await CreateProvider(http).QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        var header = Assert.Single(http.AuthorizationHeaders);
        Assert.Equal("Bearer sk-test-only-not-real", header);
        Assert.DoesNotContain(ApiKey, http.RequestUrls.Single());
        Assert.Contains("/v1/user/info", http.RequestUrls.Single());
    }
}
