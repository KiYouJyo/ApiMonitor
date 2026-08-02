using System.Net;
using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Providers;
using ApiBalanceMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiBalanceMonitor.Tests;

public sealed class DeepSeekBalanceProviderTests
{
    private const string ApiKey = "sk-test-only-not-real";

    private static ApiAccount TestAccount() =>
        new()
        {
            AccountId = "acct-test-1",
            ProviderId = "deepseek",
            DisplayName = "Test",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static DeepSeekBalanceProvider CreateProvider(FakeHttpRequestService http) =>
        new(http);

    [Fact]
    public async Task NormalResponse_ParsesSnapshot()
    {
        const string json = """
            {
              "is_available": true,
              "balance_infos": [
                { "currency": "CNY", "total_balance": "110.00", "granted_balance": "10.00", "topped_up_balance": "100.00" }
              ]
            }
            """;

        var http = FakeHttpRequestService.Returning(json);
        var result = await CreateProvider(http).QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.IsType<BalanceSnapshot>(result.Snapshot);
        Assert.True(snapshot.IsAvailable);
        Assert.Equal("acct-test-1", snapshot.AccountId);
        Assert.Single(snapshot.Balances);

        var balance = snapshot.Balances[0];
        Assert.Equal("CNY", balance.Currency);
        Assert.Equal(110.00m, balance.TotalBalance);
        Assert.Equal(10.00m, balance.GrantedBalance);
        Assert.Equal(100.00m, balance.ToppedUpBalance);
    }

    [Fact]
    public async Task ResponseWithCnyAndUsd_ParsesBothCurrencies()
    {
        const string json = """
            {
              "is_available": true,
              "balance_infos": [
                { "currency": "CNY", "total_balance": "110.00", "granted_balance": "10.00", "topped_up_balance": "100.00" },
                { "currency": "USD", "total_balance": "50.50", "granted_balance": "0.00", "topped_up_balance": "50.50" }
              ]
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshot!.Balances.Count);
        Assert.Contains(result.Snapshot.Balances, b => b.Currency == "USD" && b.TotalBalance == 50.50m);
        Assert.Contains(result.Snapshot.Balances, b => b.Currency == "CNY" && b.TotalBalance == 110.00m);
    }

    [Fact]
    public async Task IsAvailableFalse_StillParsesBalances()
    {
        const string json = """
            {
              "is_available": false,
              "balance_infos": [
                { "currency": "CNY", "total_balance": "1.50", "granted_balance": "0.50", "topped_up_balance": "1.00" }
              ]
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Snapshot!.IsAvailable);
        Assert.Single(result.Snapshot.Balances);
        Assert.Equal(1.50m, result.Snapshot.Balances[0].TotalBalance);
    }

    [Theory]
    [InlineData("110.00", 110.00)]
    [InlineData("12.345", 12.345)]
    [InlineData("1,000.50", 1000.50)]
    [InlineData("-5.25", -5.25)]
    public async Task BalanceStrings_ParseToDecimal(string value, decimal expected)
    {
        string json =
            $$"""
            {
              "is_available": true,
              "balance_infos": [
                { "currency": "CNY", "total_balance": "{{value}}", "granted_balance": "0", "topped_up_balance": "0" }
              ]
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Snapshot!.Balances[0].TotalBalance);
    }

    [Fact]
    public async Task MissingOptionalBalanceFields_DefaultsToZero()
    {
        const string json = """
            {
              "is_available": true,
              "balance_infos": [
                { "currency": "CNY", "total_balance": "88.00" }
              ]
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var balance = result.Snapshot!.Balances[0];
        Assert.Equal(88.00m, balance.TotalBalance);
        Assert.Equal(0m, balance.GrantedBalance);
        Assert.Equal(0m, balance.ToppedUpBalance);
    }

    [Fact]
    public async Task MissingBalanceInfos_ReturnsEmptySnapshot()
    {
        const string json = """{ "is_available": true }""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Snapshot!.Balances);
    }

    [Fact]
    public async Task InvalidJson_ReturnsStructuredError()
    {
        const string json = """{ "is_available": true, "balance_infos": [ { "currency": """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidJson, result.Error!.Kind);
    }

    [Fact]
    public async Task UnknownNewFields_DoNotFailParsing()
    {
        const string json = """
            {
              "is_available": true,
              "some_new_field": { "future": ["a", "b"] },
              "balance_infos": [
                {
                  "currency": "CNY",
                  "total_balance": "10.00",
                  "granted_balance": "1.00",
                  "topped_up_balance": "9.00",
                  "future_field": 123,
                  "another": null
                }
              ]
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Snapshot!.Balances);
        Assert.Equal("CNY", result.Snapshot.Balances[0].Currency);
    }

    [Fact]
    public async Task UnknownCurrency_IsKeptAsIs()
    {
        const string json = """
            {
              "is_available": true,
              "balance_infos": [
                { "currency": "XYZ", "total_balance": "7.00", "granted_balance": "0", "topped_up_balance": "7.00" }
              ]
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("XYZ", result.Snapshot!.Balances[0].Currency);
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
            """{ "is_available": true, "balance_infos": [] }""");

        await CreateProvider(http).QueryBalanceAsync(TestAccount(), ApiKey, CancellationToken.None);

        var header = Assert.Single(http.AuthorizationHeaders);
        Assert.Equal("Bearer sk-test-only-not-real", header);
        Assert.DoesNotContain(ApiKey, http.RequestUrls.Single());
    }
}
