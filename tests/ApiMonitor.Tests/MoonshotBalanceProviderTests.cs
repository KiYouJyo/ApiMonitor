using System.Net;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class MoonshotBalanceProviderTests
{
    private const string ApiKey = "sk-test-only-not-real";

    private static ApiAccount TestAccount() =>
        new()
        {
            AccountId = "acct-moonshot-1",
            ProviderId = "moonshot",
            DisplayName = "Moonshot",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static MoonshotBalanceProvider CreateProvider(FakeHttpRequestService http) =>
        new(http);

    [Fact]
    public async Task NormalResponse_ParsesAvailableCashAndVoucher()
    {
        const string json = """
            {
              "code": 0,
              "data": {
                "available_balance": 49.58894,
                "voucher_balance": 46.58893,
                "cash_balance": 3.00001
              },
              "scode": "0x0",
              "status": true
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snapshot = result.Snapshot!;
        Assert.True(snapshot.IsAvailable);
        Assert.Equal("acct-moonshot-1", snapshot.AccountId);

        var available = Assert.Single(snapshot.Metrics, m => m.MetricId == "moonshot:balance.available.cny");
        Assert.Equal(49.58894m, available.AvailableAmount);
        Assert.Equal(49.58894m, available.TotalAmount);
        Assert.Equal(46.58893m, available.GrantedAmount);
        Assert.Equal(3.00001m, available.ToppedUpAmount);
        Assert.True(available.IsThresholdSupported);

        Assert.Equal(3.00001m, Assert.Single(snapshot.Metrics, m => m.MetricId == "moonshot:balance.cash.cny").AvailableAmount);
        Assert.Equal(46.58893m, Assert.Single(snapshot.Metrics, m => m.MetricId == "moonshot:balance.voucher.cny").AvailableAmount);
    }

    [Theory]
    [InlineData("49.58894", 49.58894)]
    [InlineData("12.345", 12.345)]
    [InlineData("-5.25", -5.25)]
    [InlineData("1,000.50", 1000.50)]
    public async Task NumericStrings_ParseToDecimal(string value, decimal expected)
    {
        string json =
            $$"""
            {
              "code": 0,
              "data": {
                "available_balance": "{{value}}",
                "voucher_balance": "0",
                "cash_balance": "0"
              },
              "scode": "0x0",
              "status": true
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Snapshot!.Metrics[0].AvailableAmount);
    }

    [Fact]
    public async Task MissingBreakdownFields_AreNullNotZero()
    {
        const string json = """
            {
              "code": 0,
              "data": { "available_balance": 88.00 },
              "scode": "0x0",
              "status": true
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var available = result.Snapshot!.Metrics.Single();
        Assert.Equal(88.00m, available.AvailableAmount);
        Assert.Null(available.GrantedAmount);
        Assert.Null(available.ToppedUpAmount);
        Assert.DoesNotContain(result.Snapshot.Metrics, m => m.MetricId == "moonshot:balance.cash.cny");
        Assert.DoesNotContain(result.Snapshot.Metrics, m => m.MetricId == "moonshot:balance.voucher.cny");
    }

    [Fact]
    public async Task ZeroOrNegativeAvailable_IsNotAvailable()
    {
        const string json = """
            {
              "code": 0,
              "data": { "available_balance": 0, "voucher_balance": 0, "cash_balance": -3.50 },
              "scode": "0x0",
              "status": true
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Snapshot!.IsAvailable);
        Assert.Equal(0m, result.Snapshot.Metrics[0].AvailableAmount);
        Assert.Equal(-3.50m, Assert.Single(result.Snapshot.Metrics, m => m.MetricId == "moonshot:balance.cash.cny").AvailableAmount);
    }

    [Fact]
    public async Task MissingAvailableBalance_ReturnsInvalidResponse()
    {
        const string json = """
            {
              "code": 0,
              "data": { "voucher_balance": 1.00, "cash_balance": 2.00 },
              "scode": "0x0",
              "status": true
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.DoesNotContain(ApiKey, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingData_ReturnsInvalidResponse()
    {
        const string json = """{ "code": 0, "scode": "0x0", "status": true }""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public async Task NonZeroCode_ReturnsInvalidResponse()
    {
        const string json = """
            {
              "code": 1001,
              "data": { "available_balance": 10.00 },
              "scode": "0x3E9",
              "status": false
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public async Task UnknownNewFields_DoNotFailParsing()
    {
        const string json = """
            {
              "code": 0,
              "data": {
                "available_balance": 10.00,
                "voucher_balance": 5.00,
                "cash_balance": 5.00,
                "future_field": { "a": [1, 2] }
              },
              "scode": "0x0",
              "status": true,
              "extra": "ignored"
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10.00m, result.Snapshot!.Metrics[0].AvailableAmount);
    }

    [Fact]
    public async Task InvalidJson_ReturnsInvalidJson()
    {
        const string json = """{ "code": 0, "data": { "available_balance": """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
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
    [InlineData(HttpStatusCode.BadGateway, BalanceErrorKind.ServerError)]
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
        var http = FakeHttpRequestService.Returning("{}", HttpStatusCode.Unauthorized);
        var result = await CreateProvider(http)
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(ApiKey, result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeout_ReturnsTimeoutError()
    {
        var result = await CreateProvider(FakeHttpRequestService.Throwing<TaskCanceledException>())
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.Timeout, result.Error!.Kind);
    }

    [Fact]
    public async Task NetworkFailure_ReturnsNetworkError()
    {
        var result = await CreateProvider(FakeHttpRequestService.Throwing<HttpRequestException>())
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.Network, result.Error!.Kind);
    }

    [Fact]
    public async Task Cancellation_IsPropagatedNotClassified()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateProvider(FakeHttpRequestService.Throwing<TaskCanceledException>())
                .QueryBalanceAsync(TestAccount(), ApiKey, cts.Token));
    }

    [Fact]
    public async Task ApiKeyGoesIntoAuthorizationHeader_NotIntoUrl()
    {
        var http = FakeHttpRequestService.Returning(
            """{ "code": 0, "data": { "available_balance": 1.00 }, "scode": "0x0", "status": true }""");

        await CreateProvider(http).QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        var header = Assert.Single(http.AuthorizationHeaders);
        Assert.Equal("Bearer sk-test-only-not-real", header);
        Assert.DoesNotContain(ApiKey, http.RequestUrls.Single());
        Assert.Contains("api.moonshot.cn", http.RequestUrls.Single());
        Assert.Contains("/v1/users/me/balance", http.RequestUrls.Single());
    }
}
