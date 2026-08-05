using System.Net;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class XaiBalanceProviderTests
{
    private const string ApiKey = "xai-test-only-not-real";
    private const string TeamId = "65c1e471-205f-4566-9c5a-07198bcdf4ce";

    private static ApiAccount Account(string? teamId = TeamId) =>
        new()
        {
            AccountId = "acct-xai-1",
            ProviderId = "xai",
            DisplayName = "xAI",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CredentialMode = XaiBalanceProvider.ManagementKeyMode,
            ProviderConfig = teamId is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [XaiBalanceProvider.TeamIdField] = teamId,
                },
        };

    private static XaiBalanceProvider CreateProvider(FakeHttpRequestService http) =>
        new(http);

    [Fact]
    public async Task PrepaidBalance_CentsConvertToUsd_WithDocumentedSign()
    {
        // 官方示例：PURCHASE 1000 美分后 total=-1000（账务方向，负值=持有 Credits）。
        // 用户可用余额 = -(-1000)/100 = 10.00 美元。
        const string json = """{ "changes": [], "total": { "val": "-1000" } }""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var metric = Assert.Single(result.Snapshot!.Metrics);
        Assert.Equal("xai:balance.prepaid.usd", metric.MetricId);
        Assert.Equal(10.00m, metric.AvailableAmount);
        Assert.Equal("USD", metric.Unit);
        Assert.True(result.Snapshot.IsAvailable);
    }

    [Fact]
    public async Task NegativeRemaining_IsPreservedNotClamped()
    {
        // 透支：total.val=500 美分 -> 用户欠 5.00 美元，必须保留负值。
        const string json = """{ "changes": [], "total": { "val": "500" } }""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(-5.00m, result.Snapshot!.Metrics[0].AvailableAmount);
        Assert.False(result.Snapshot.IsAvailable);
    }

    [Fact]
    public async Task Cents_AreNotDisplayedAsWholeDollars()
    {
        const string json = """{ "changes": [], "total": { "val": "-10050" } }""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100.50m, result.Snapshot!.Metrics[0].AvailableAmount);
    }

    [Fact]
    public async Task ZeroBalance_IsZero()
    {
        const string json = """{ "changes": [], "total": { "val": "0" } }""";

        var result = await CreateProvider(FakeHttpRequestService.Returning(json))
            .QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Snapshot!.Metrics[0].AvailableAmount);
    }

    [Fact]
    public async Task TeamIdMissing_ReturnsConfigurationMissing()
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning("{}"))
            .QueryBalanceAsync(Account(teamId: null), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.ConfigurationMissing, result.Error!.Kind);
        Assert.Contains("Team ID", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TeamId_IsPathEncoded()
    {
        var http = FakeHttpRequestService.Returning("""{ "total": { "val": "-1000" } }""");

        await CreateProvider(http).QueryBalanceAsync(
            Account("a b/c?d=e"),
            ApiKey,
            CancellationToken.None);

        string url = http.RequestUrls.Single();
        Assert.Contains("/v1/billing/teams/a%20b%2Fc%3Fd%3De/prepaid/balance", url);
        Assert.DoesNotContain("a b/c?d=e", url);
    }

    [Fact]
    public async Task ManagementKey_GoesOnlyToManagementApiPrepaidEndpoint()
    {
        var http = FakeHttpRequestService.Returning("""{ "total": { "val": "-1000" } }""");

        await CreateProvider(http).QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);

        string url = http.RequestUrls.Single();
        Assert.StartsWith("https://management-api.x.ai/v1/billing/teams/", url);
        Assert.Contains("/prepaid/balance", url);
        var header = Assert.Single(http.AuthorizationHeaders);
        Assert.Equal($"Bearer {ApiKey}", header);
    }

    [Fact]
    public async Task BalanceStructureMissing_ReturnsUnsupportedBalanceFormat()
    {
        var provider = CreateProvider(FakeHttpRequestService.Returning("""{ "changes": [] }"""));
        var result = await provider.QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result.Error!.Kind);

        var provider2 = CreateProvider(FakeHttpRequestService.Returning("""{ "total": { "other": "1" } }"""));
        var result2 = await provider2.QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);
        Assert.False(result2.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result2.Error!.Kind);

        var provider3 = CreateProvider(FakeHttpRequestService.Returning("""{ "total": { "val": "abc" } }"""));
        var result3 = await provider3.QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);
        Assert.False(result3.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidResponse, result3.Error!.Kind);
    }

    [Fact]
    public async Task InvalidJson_ReturnsInvalidJson()
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning("""{ "total": { "val": """))
            .QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidJson, result.Error!.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BalanceErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, BalanceErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, BalanceErrorKind.AccountNotFound)]
    [InlineData((HttpStatusCode)429, BalanceErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, BalanceErrorKind.ServerError)]
    public async Task HttpErrorStatus_ReturnsClassifiedError(HttpStatusCode status, BalanceErrorKind expectedKind)
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning("{}", status))
            .QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedKind, result.Error!.Kind);
    }

    [Fact]
    public async Task WrongKeyType_ErrorHintsManagementKey_WithoutLeakingKey()
    {
        const string normalModelKey = "xai-model-key-not-a-management-key";
        var result = await CreateProvider(FakeHttpRequestService.Returning("{}", HttpStatusCode.Unauthorized))
            .QueryBalanceAsync(Account(), normalModelKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.Unauthorized, result.Error!.Kind);
        Assert.Contains("Management Key", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(normalModelKey, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Forbidden_HintsBillingPermission_WithoutLeakingKey()
    {
        var result = await CreateProvider(FakeHttpRequestService.Returning("{}", HttpStatusCode.Forbidden))
            .QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.Forbidden, result.Error!.Kind);
        Assert.Contains("Management Key", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiKey, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeout_ReturnsTimeoutError()
    {
        var result = await CreateProvider(FakeHttpRequestService.Throwing<TaskCanceledException>())
            .QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.Timeout, result.Error!.Kind);
    }

    [Fact]
    public async Task NetworkFailure_ReturnsNetworkError()
    {
        var result = await CreateProvider(FakeHttpRequestService.Throwing<HttpRequestException>())
            .QueryBalanceAsync(Account(), ApiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.Network, result.Error!.Kind);
    }
}
