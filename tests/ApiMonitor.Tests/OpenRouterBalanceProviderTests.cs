using System.Net;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class OpenRouterBalanceProviderTests
{
    private const string ApiKey = "sk-or-v1-test-only-not-real";

    private static ApiAccount Account(string mode) =>
        new()
        {
            AccountId = "acct-or-1",
            ProviderId = "openrouter",
            DisplayName = "OR 测试",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CredentialMode = mode,
        };

    private static OpenRouterBalanceProvider CreateProvider(FakeHttpRequestService http) =>
        new(http);


    [Fact]
    public async Task ApiKeyMode_ParsesQuotaAndUsageMetrics()
    {
        const string json = """
            {
              "data": {
                "label": "work",
                "limit": 1000,
                "limit_reset": "2026-08-03T00:00:00Z",
                "limit_remaining": 876.55,
                "usage": "123.45",
                "usage_daily": "10.00",
                "usage_weekly": "50.00",
                "usage_monthly": "123.45",
                "byok_usage": "2.50",
                "is_free_tier": false
              }
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ApiKeyMode), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var metrics = result.Snapshot!.Metrics;
        var remaining = Assert.Single(metrics, m => m.MetricId == "openrouter:key:quota-remaining");
        Assert.Equal(876.55m, remaining.AvailableAmount);
        Assert.Equal(1000m, remaining.TotalAmount);
        Assert.True(remaining.IsThresholdSupported);
        Assert.False(remaining.IsUnlimited);
        Assert.Equal(BalanceMetricKind.KeyQuota, remaining.Kind);

        var limit = Assert.Single(metrics, m => m.MetricId == "openrouter:key:quota-limit");
        Assert.Equal(1000m, limit.TotalAmount);

        Assert.Equal(123.45m, Assert.Single(metrics, m => m.MetricId == "openrouter:key:usage-total").UsedAmount);
        Assert.Equal(10.00m, Assert.Single(metrics, m => m.MetricId == "openrouter:key:usage-daily").UsedAmount);
        Assert.Equal(50.00m, Assert.Single(metrics, m => m.MetricId == "openrouter:key:usage-weekly").UsedAmount);
        Assert.Equal(123.45m, Assert.Single(metrics, m => m.MetricId == "openrouter:key:usage-monthly").UsedAmount);
        Assert.Equal(2.50m, Assert.Single(metrics, m => m.MetricId == "openrouter:key:usage-byok").UsedAmount);
    }

    [Fact]
    public async Task ApiKeyMode_NullLimitRemaining_IsUnlimitedNotZero()
    {
        const string json = """
            {
              "data": {
                "label": "no-limit",
                "limit": null,
                "limit_remaining": null,
                "usage": "5.00"
              }
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ApiKeyMode), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var remaining = Assert.Single(result.Snapshot!.Metrics, m => m.MetricId == "openrouter:key:quota-remaining");
        Assert.Null(remaining.AvailableAmount);
        Assert.Null(remaining.TotalAmount);
        Assert.True(remaining.IsUnlimited);
        Assert.False(remaining.IsThresholdSupported);
    }

    [Fact]
    public async Task ApiKeyMode_MissingAllNumericFields_ReturnsInvalidResponse()
    {
        const string json = """{ "data": { "label": "empty" } }""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ApiKeyMode), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public async Task ApiKeyMode_MissingData_ReturnsInvalidResponse()
    {
        const string json = """{ "some_new_field": true }""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ApiKeyMode), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public async Task ManagementKeyMode_ComputesRemainingCredits()
    {
        const string json = """
            {
              "total_credits": "10.00",
              "total_usage": "5.75"
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ManagementKeyMode), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var remaining = Assert.Single(result.Snapshot!.Metrics, m => m.MetricId == "openrouter:credits:remaining");
        Assert.Equal(4.25m, remaining.AvailableAmount);
        Assert.Equal(10.00m, remaining.TotalAmount);
        Assert.Equal(5.75m, remaining.UsedAmount);
        Assert.True(remaining.IsThresholdSupported);

        Assert.Equal(10.00m, Assert.Single(result.Snapshot.Metrics, m => m.MetricId == "openrouter:credits:total").TotalAmount);
        Assert.Equal(5.75m, Assert.Single(result.Snapshot.Metrics, m => m.MetricId == "openrouter:credits:usage").UsedAmount);
    }

    [Fact]
    public async Task ManagementKeyMode_NegativeRemaining_IsNotClampedToZero()
    {
        const string json = """{ "total_credits": "5.00", "total_usage": "8.00" }""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ManagementKeyMode), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var remaining = Assert.Single(result.Snapshot!.Metrics, m => m.MetricId == "openrouter:credits:remaining");
        Assert.Equal(-3.00m, remaining.AvailableAmount);
    }

    [Fact]
    public async Task ManagementKeyMode_MissingTotalCredits_ReturnsInvalidResponse()
    {
        const string json = """{ "total_usage": "5.00" }""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ManagementKeyMode), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);
    }

    [Fact]
    public async Task ForbiddenOnCreditsEndpoint_HintsManagementKey()
    {
        var result = await CreateProvider(
            FakeHttpRequestService.Returning("{}", HttpStatusCode.Forbidden))
            .QueryBalanceAsync(
                Account(OpenRouterBalanceProvider.ManagementKeyMode),
                ApiKey,
                CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.Forbidden, result.Error!.Kind);
        Assert.Contains("Management Key", result.Error!.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BalanceErrorKind.Unauthorized)]
    [InlineData((HttpStatusCode)402, BalanceErrorKind.PaymentRequired)]
    [InlineData((HttpStatusCode)429, BalanceErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, BalanceErrorKind.ServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, BalanceErrorKind.ServerError)]
    public async Task HttpErrorStatus_ReturnsClassifiedError(HttpStatusCode status, BalanceErrorKind expectedKind)
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning("{}", status))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ApiKeyMode), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedKind, result.Error!.Kind);
    }

    [Fact]
    public async Task UnknownNewFields_DoNotFailParsing()
    {
        const string json = """
            {
              "data": {
                "label": "x",
                "limit": 100,
                "limit_remaining": 50,
                "usage": "10",
                "brand_new_field": { "nested": [1, 2, 3] }
              },
              "new_top_level": "future"
            }
            """;

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ApiKeyMode), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Snapshot!.Metrics, m => m.MetricId == "openrouter:key:quota-remaining");
    }

    [Fact]
    public async Task EmptyBody_ReturnsEmptyContentError()
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning(string.Empty))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ApiKeyMode), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.EmptyContent, result.Error!.Kind);
    }

    [Fact]
    public async Task InvalidJson_ReturnsInvalidJsonError()
    {
        const string json = """{ "data": [""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ApiKeyMode), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidJson, result.Error!.Kind);
    }

    [Fact]
    public async Task TimeoutAndNetwork_AreClassified()
    {
        var timeout = await CreateProvider(FakeHttpRequestService.Throwing<TaskCanceledException>())
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ApiKeyMode), ApiKey, CancellationToken.None);
        Assert.Equal(BalanceErrorKind.Timeout, timeout.Error!.Kind);

        var network = await CreateProvider(FakeHttpRequestService.Throwing<HttpRequestException>())
            .QueryBalanceAsync(Account(OpenRouterBalanceProvider.ApiKeyMode), ApiKey, CancellationToken.None);
        Assert.Equal(BalanceErrorKind.Network, network.Error!.Kind);
    }

    [Fact]
    public async Task ApiKeyGoesIntoAuthorizationHeader_NotIntoUrl()
    {
        var http = FakeHttpRequestService.Returning("""{ "data": { "limit": 1, "limit_remaining": 1 } }""");

        await CreateProvider(http).QueryBalanceAsync(
            Account(OpenRouterBalanceProvider.ApiKeyMode),
            ApiKey,
            CancellationToken.None);

        var header = Assert.Single(http.AuthorizationHeaders);
        Assert.Equal("Bearer " + ApiKey, header);
        Assert.DoesNotContain(ApiKey, http.RequestUrls.Single());
        Assert.Contains("openrouter.ai/api/v1/key", http.RequestUrls.Single());
    }

    [Fact]
    public async Task ManagementKey_UsesCreditsEndpointOnly()
    {
        var http = FakeHttpRequestService.Returning("""{ "total_credits": "10.00", "total_usage": "1.00" }""");

        await CreateProvider(http).QueryBalanceAsync(
            Account(OpenRouterBalanceProvider.ManagementKeyMode),
            ApiKey,
            CancellationToken.None);

        Assert.Single(http.RequestUrls);
        Assert.Contains("openrouter.ai/api/v1/credits", http.RequestUrls.Single());
    }

    [Fact]
    public void ProviderInfo_DeclaresBothCredentialModesAndCapabilities()
    {
        var info = CreateProvider(FakeHttpRequestService.Returning("{}")).Info;

        Assert.Equal("openrouter", info.ProviderId);
        Assert.Equal("OpenRouter", info.DisplayName);
        Assert.True(info.SupportsAccountBalance);
        Assert.True(info.SupportsKeyQuota);
        Assert.Equal(2, info.CredentialOptions.Count);
        Assert.Contains(info.CredentialOptions, o => o.CredentialTypeId == OpenRouterBalanceProvider.ApiKeyMode);
        Assert.Contains(info.CredentialOptions, o => o.CredentialTypeId == OpenRouterBalanceProvider.ManagementKeyMode);
    }
}
