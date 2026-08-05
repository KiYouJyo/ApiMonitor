using System.Net;
using System.Text;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// SuperMap iServer 自托管探测测试（全部使用 Mock）。
/// </summary>
public sealed class SuperMapIServerProviderTests
{
    private const string BaseUrl = "https://gis.example.test:8090";

    private static ApiAccount Account(
        string expectedService = "",
        bool allowHttp = false,
        bool enableManager = false,
        string? token = null,
        string? baseUrlOverride = null) =>
        new()
        {
            AccountId = "acct-supermap",
            ProviderId = "supermap-iserver",
            DisplayName = "iServer",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ProviderConfig = new Dictionary<string, string>
            {
                [SuperMapIServerProvider.BaseUrlField] = baseUrlOverride ?? BaseUrl,
                [SuperMapIServerProvider.ExpectedServiceField] = expectedService,
                [SuperMapIServerProvider.AllowHttpField] = allowHttp ? "true" : "false",
                [SuperMapIServerProvider.EnableManagerStatusField] = enableManager ? "true" : "false",
            },
            CredentialSlots = token is null
                ? new Dictionary<string, bool>()
                : new Dictionary<string, bool> { [CredentialSlots.QueryToken] = true },
        };

    private static Dictionary<string, string> Credentials(string? token = null)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (token is not null)
        {
            dict[CredentialSlots.QueryToken] = token;
        }

        return dict;
    }

    private static string CatalogJson(params string[] names)
    {
        string services = string.Join(
            ",",
            names.Select(n => $$"""{ "name": "{{n}}", "url": "{{BaseUrl}}/iserver/services/{{n}}", "type": "REST" }"""));
        return $$"""{ "services": [ {{services}} ] }""";
    }

    private static async Task<BalanceQueryResult> QueryAsync(
        FakeHttpRequestService http,
        ApiAccount account,
        string? token = null)
    {
        var provider = new SuperMapIServerProvider(http);
        return await provider.QueryBalanceAsync(account, Credentials(token), CancellationToken.None);
    }

    [Fact]
    public async Task CatalogNormal_ParsesCountAndExpectedService()
    {
        var http = FakeHttpRequestService.Returning(CatalogJson("rest", "map-world"));

        var result = await QueryAsync(http, Account(expectedService: "map-world"));

        Assert.True(result.IsSuccess);
        var metrics = result.Snapshot!.Metrics;
        Assert.Equal("Healthy", metrics.First(m => m.MetricId == "supermap-iserver:service.availability").StatusValue);
        Assert.Equal(2L, metrics.First(m => m.MetricId == "supermap-iserver:services.count").IntegerValue);
        Assert.True(metrics.First(m => m.MetricId == "supermap-iserver:expected-service.present").BooleanValue);
    }

    [Fact]
    public async Task EmptyCatalog_IsNotOffline()
    {
        var http = FakeHttpRequestService.Returning("""{ "services": [] }""");

        var result = await QueryAsync(http, Account());

        Assert.True(result.IsSuccess);
        Assert.True(result.Snapshot!.IsAvailable);
        Assert.Equal(0L, result.Snapshot.Metrics.First(m => m.MetricId == "supermap-iserver:services.count").IntegerValue);
    }

    [Fact]
    public async Task ExpectedServiceMissing_SetsBooleanFalse()
    {
        var http = FakeHttpRequestService.Returning(CatalogJson("rest"));

        var result = await QueryAsync(http, Account(expectedService: "missing-service"));

        Assert.True(result.IsSuccess);
        Assert.False(result.Snapshot!.Metrics.First(m =>
            m.MetricId == "supermap-iserver:expected-service.present").BooleanValue);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BalanceErrorKind.CredentialInvalid)]
    [InlineData(HttpStatusCode.Forbidden, BalanceErrorKind.PermissionDenied)]
    [InlineData(HttpStatusCode.NotFound, BalanceErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, BalanceErrorKind.ServerError)]
    [InlineData(HttpStatusCode.Redirect, BalanceErrorKind.RedirectBlocked)]
    public async Task HttpErrors_AreClassified(HttpStatusCode status, BalanceErrorKind expected)
    {
        var result = await QueryAsync(
            FakeHttpRequestService.Returning("{}", status),
            Account());

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Error!.Kind);
    }

    [Fact]
    public async Task Timeout_ReturnsTimeout()
    {
        var result = await QueryAsync(
            FakeHttpRequestService.Throwing<TaskCanceledException>(),
            Account());

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.Timeout, result.Error!.Kind);
    }

    [Fact]
    public async Task HttpBaseUrl_WithoutConfirmation_IsRejectedBeforeSend()
    {
        var http = FakeHttpRequestService.Returning(CatalogJson("rest"));
        var account = Account(
            allowHttp: false,
            baseUrlOverride: "http://gis.example.test:8090");

        var result = await QueryAsync(http, account);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.ProtocolViolation, result.Error!.Kind);
        Assert.Empty(http.RequestUrls);
    }

    [Fact]
    public async Task FileScheme_IsRejected()
    {
        var http = FakeHttpRequestService.Returning(CatalogJson("rest"));
        var account = Account(baseUrlOverride: "file:///C:/data");

        var result = await QueryAsync(http, account);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.ProtocolViolation, result.Error!.Kind);
        Assert.Empty(http.RequestUrls);
    }

    [Fact]
    public async Task HttpAllowed_AndToken_AppendsTokenToCatalogUrl()
    {
        var http = FakeHttpRequestService.Returning(CatalogJson("rest"));

        var result = await QueryAsync(http, Account(allowHttp: true), token: "super-secret-token");

        Assert.True(result.IsSuccess);
        var url = Assert.Single(http.RequestUrls);
        Assert.StartsWith("https://gis.example.test:8090/iserver/services.json", url);
        Assert.Contains("token=super-secret-token", url);
    }

    [Fact]
    public async Task ErrorMessage_DoesNotContainTokenOrCatalogContent()
    {
        var http = FakeHttpRequestService.Returning("{}", HttpStatusCode.Unauthorized);

        var result = await QueryAsync(http, Account(expectedService: "rest"), token: "super-secret-token");

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("super-secret-token", result.Error!.Message);
        Assert.DoesNotContain("map-world", result.Error.Message);
    }

    [Fact]
    public async Task ManagerStatusProbe_OnlyWhenEnabled()
    {
        string catalog = CatalogJson("rest");
        string managerJson = """{ "state": "RUNNING", "cpuUsage": 0.2 }""";
        var http = FakeHttpRequestService.Mutable(catalog);
        var requests = new List<string>();
        http.SetHandler((request, _) =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            string body = request.RequestUri!.AbsoluteUri.Contains("serverstatus")
                ? managerJson
                : catalog;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        });

        var result = await QueryAsync(http, Account(enableManager: true));

        Assert.True(result.IsSuccess);
        Assert.Contains(requests, u => u.Contains("/iserver/services.json"));
        Assert.Contains(requests, u => u.Contains("/iserver/manager/serverstatus.json"));
        Assert.Contains(result.Snapshot!.Metrics, m =>
            m.MetricId == "supermap-iserver:server.status" && m.StatusValue == "Healthy");
    }

    [Fact]
    public async Task ManagerStatus_OffByDefault_NoManagerRequest()
    {
        var http = FakeHttpRequestService.Mutable(CatalogJson("rest"));
        var requests = new List<string>();
        http.SetHandler((request, _) =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CatalogJson("rest"), Encoding.UTF8, "application/json"),
            });
        });

        await QueryAsync(http, Account(enableManager: false));

        Assert.DoesNotContain(requests, u => u.Contains("serverstatus"));
    }
}
