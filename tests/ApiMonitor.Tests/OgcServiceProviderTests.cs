using System.Net;
using System.Text;
using ApiMonitor.Models;
using ApiMonitor.Providers;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// 通用 OGC 服务探测测试（全部使用 Mock；验证安全 XML 解析与 GetCapabilities 结构）。
/// </summary>
public sealed class OgcServiceProviderTests
{
    private static readonly string BaseUrl =
        "https://gis.example.test/geoserver/wms?service=WMS&request=GetCapabilities";

    private static ApiAccount Account(
        string authMode = OgcServiceProvider.AuthNone,
        string expectedLayer = "",
        bool allowHttp = false,
        string? capabilitiesUrlOverride = null) =>
        new()
        {
            AccountId = "acct-ogc",
            ProviderId = "ogc-service",
            DisplayName = "OGC",
            HasCredential = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ProviderConfig = new Dictionary<string, string>
            {
                [OgcServiceProvider.ServiceTypeField] = "wms",
                [OgcServiceProvider.CapabilitiesUrlField] = capabilitiesUrlOverride ?? BaseUrl,
                [OgcServiceProvider.ExpectedLayerField] = expectedLayer,
                [OgcServiceProvider.AuthModeField] = authMode,
                [OgcServiceProvider.AllowHttpField] = allowHttp ? "true" : "false",
            },
        };

    private static Dictionary<string, string> Credentials(params (string Slot, string Value)[] entries)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (slot, value) in entries)
        {
            dict[slot] = value;
        }

        return dict;
    }

    private static async Task<BalanceQueryResult> QueryAsync(
        FakeHttpRequestService http,
        ApiAccount account,
        Dictionary<string, string>? credentials = null)
    {
        var provider = new OgcServiceProvider(http);
        return await provider.QueryBalanceAsync(
            account,
            credentials ?? Credentials(),
            CancellationToken.None);
    }

    private const string Wms111 = """
        <?xml version="1.0" encoding="UTF-8"?>
        <WMT_MS_Capabilities version="1.1.1">
          <Service><Name>OGC:WMS</Name></Service>
          <Capability>
            <Layer><Title>Root</Title>
              <Layer><Name>roads</Name><Title>Roads</Title></Layer>
              <Layer><Name>rivers</Name><Title>Rivers</Title></Layer>
            </Layer>
          </Capability>
        </WMT_MS_Capabilities>
        """;

    private const string Wms130 = """
        <?xml version="1.0" encoding="UTF-8"?>
        <WMS_Capabilities version="1.3.0" xmlns="http://www.opengis.net/wms">
          <Service><Name>WMS</Name></Service>
          <Capability>
            <Layer><Title>Root</Title>
              <Layer><Name>forest</Name></Layer>
            </Layer>
          </Capability>
        </WMS_Capabilities>
        """;

    private const string Wmts = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Capabilities version="1.0.0" xmlns="http://www.opengis.net/wmts/1.0">
          <Contents>
            <Layer><ows:Identifier xmlns:ows="http://www.opengis.net/ows/1.1">tiles-a</ows:Identifier></Layer>
            <Layer><ows:Identifier xmlns:ows="http://www.opengis.net/ows/1.1">tiles-b</ows:Identifier></Layer>
          </Contents>
        </Capabilities>
        """;

    private const string Wfs100 = """
        <?xml version="1.0" encoding="UTF-8"?>
        <WFS_Capabilities version="1.0.0">
          <FeatureTypeList>
            <FeatureType><Name>parcels</Name></FeatureType>
            <FeatureType><Name>buildings</Name></FeatureType>
          </FeatureTypeList>
        </WFS_Capabilities>
        """;

    private const string Wfs200 = """
        <?xml version="1.0" encoding="UTF-8"?>
        <wfs:WFS_Capabilities version="2.0.0" xmlns:wfs="http://www.opengis.net/wfs/2.0">
          <wfs:FeatureTypeList>
            <wfs:FeatureType><wfs:Name>land</wfs:Name></wfs:FeatureType>
          </wfs:FeatureTypeList>
        </wfs:WFS_Capabilities>
        """;

    [Theory]
    [InlineData(Wms111, "WMS", "1.1.1", 2)]
    [InlineData(Wms130, "WMS", "1.3.0", 1)]
    [InlineData(Wmts, "WMTS", "1.0.0", 2)]
    [InlineData(Wfs100, "WFS", "1.0.0", 2)]
    [InlineData(Wfs200, "WFS", "2.0.0", 1)]
    public async Task Capabilities_ParseTypeVersionAndLayerCount(
        string xml,
        string serviceType,
        string serviceVersion,
        long layerCount)
    {
        var http = FakeHttpRequestService.Returning(xml);

        var result = await QueryAsync(http, Account());

        Assert.True(result.IsSuccess);
        Assert.True(result.Snapshot!.IsAvailable);
        Assert.Equal("Healthy", result.Snapshot.Metrics
            .First(m => m.MetricId == "ogc-service:service.availability").StatusValue);
        Assert.Equal(serviceType, result.Snapshot.Metrics
            .First(m => m.MetricId == "ogc-service:service.type").StatusValue);
        Assert.Equal(serviceVersion, result.Snapshot.Metrics
            .First(m => m.MetricId == "ogc-service:service.version").StatusValue);
        Assert.Equal(layerCount, result.Snapshot.Metrics
            .First(m => m.MetricId == "ogc-service:layers.count").IntegerValue);
    }

    [Fact]
    public async Task ExpectedLayer_PresentAndMissing()
    {
        var http = FakeHttpRequestService.Returning(Wms111);

        var present = await QueryAsync(http, Account(expectedLayer: "roads"));
        Assert.True(present.Snapshot!.Metrics.First(m =>
            m.MetricId == "ogc-service:expected-layer.present").BooleanValue);

        var missing = await QueryAsync(http, Account(expectedLayer: "ghost"));
        Assert.False(missing.Snapshot!.Metrics.First(m =>
            m.MetricId == "ogc-service:expected-layer.present").BooleanValue);
    }

    [Fact]
    public async Task ServiceExceptionReport_IsNotHealthy()
    {
        const string xml = """
            <?xml version="1.0"?>
            <ServiceExceptionReport version="1.1.1">
              <ServiceException code="InvalidUpdateSequence">bad request</ServiceException>
            </ServiceExceptionReport>
            """;

        var result = await QueryAsync(FakeHttpRequestService.Returning(xml), Account());

        Assert.True(result.IsSuccess);
        Assert.False(result.Snapshot!.IsAvailable);
        Assert.Equal("InvalidResponse", result.Snapshot.Metrics
            .First(m => m.MetricId == "ogc-service:service.availability").StatusValue);
    }

    [Fact]
    public async Task NonXml_ReturnsInvalidXml()
    {
        const string body = "this is not xml at all";

        var result = await QueryAsync(FakeHttpRequestService.Returning(body), Account());

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidXml, result.Error!.Kind);
    }

    [Fact]
    public async Task Dtd_IsProhibited()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE WMT_MS_Capabilities SYSTEM "http://schemas.opengis.net/wms/1.1.1/WMS_MS_Capabilities.dtd">
            <WMT_MS_Capabilities version="1.1.1"></WMT_MS_Capabilities>
            """;

        var result = await QueryAsync(FakeHttpRequestService.Returning(xml), Account());

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidXml, result.Error!.Kind);
    }

    [Fact]
    public async Task ExternalEntity_IsProhibited()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <WMS_Capabilities version="1.3.0">&xxe;</WMS_Capabilities>
            """;

        var result = await QueryAsync(FakeHttpRequestService.Returning(xml), Account());

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidXml, result.Error!.Kind);
    }

    [Fact]
    public async Task EntityExpansion_IsProhibited()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE foo [ <!ENTITY a "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"> ]>
            <WMS_Capabilities version="1.3.0">&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;</WMS_Capabilities>
            """;

        var result = await QueryAsync(FakeHttpRequestService.Returning(xml), Account());

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.InvalidXml, result.Error!.Kind);
    }

    [Fact]
    public async Task OversizedResponse_IsRejected()
    {
        string big = new('a', ApiMonitor.Services.SecureXml.MaxDocumentBytes + 1);
        string xml = "<WMS_Capabilities version=\"1.3.0\">" + big + "</WMS_Capabilities>";

        var result = await QueryAsync(FakeHttpRequestService.Returning(xml), Account());

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.TooLarge, result.Error!.Kind);
    }

    [Fact]
    public async Task WrongVersion_IsInvalidResponse()
    {
        const string xml = """
            <?xml version="1.0"?>
            <WMS_Capabilities version="9.9.9"><Service/><Capability/></WMS_Capabilities>
            """;

        var result = await QueryAsync(FakeHttpRequestService.Returning(xml), Account());

        Assert.True(result.IsSuccess);
        Assert.Equal("InvalidResponse", result.Snapshot!.Metrics
            .First(m => m.MetricId == "ogc-service:service.availability").StatusValue);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BalanceErrorKind.CredentialInvalid)]
    [InlineData(HttpStatusCode.Forbidden, BalanceErrorKind.PermissionDenied)]
    [InlineData(HttpStatusCode.NotFound, BalanceErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, BalanceErrorKind.ServerError)]
    [InlineData(HttpStatusCode.Redirect, BalanceErrorKind.RedirectBlocked)]
    public async Task HttpErrors_AreClassified(HttpStatusCode status, BalanceErrorKind expected)
    {
        var result = await QueryAsync(FakeHttpRequestService.Returning("{}", status), Account());

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Error!.Kind);
    }

    [Fact]
    public async Task BasicAuth_DoesNotLeakPasswordIntoUrl()
    {
        var http = FakeHttpRequestService.Returning(Wms111);
        var account = Account(authMode: OgcServiceProvider.AuthBasic);

        var result = await QueryAsync(
            http,
            account,
            Credentials(
                (CredentialSlots.Username, "alice"),
                (CredentialSlots.Password, "super-secret-password")));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("super-secret-password", Assert.Single(http.RequestUrls));
        var auth = Assert.Single(http.AuthorizationHeaders);
        Assert.StartsWith("Basic ", auth);
        Assert.DoesNotContain("super-secret-password", auth);
    }

    [Fact]
    public async Task BearerAuth_DoesNotLeakTokenIntoUrl()
    {
        var http = FakeHttpRequestService.Returning(Wms111);

        var result = await QueryAsync(
            http,
            Account(authMode: OgcServiceProvider.AuthBearer),
            Credentials((CredentialSlots.BearerToken, "bearer-secret")));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("bearer-secret", Assert.Single(http.RequestUrls));
        Assert.Equal("Bearer bearer-secret", Assert.Single(http.AuthorizationHeaders));
    }

    [Fact]
    public async Task QueryTokenAuth_AppendsTokenAsQueryParameter()
    {
        var http = FakeHttpRequestService.Returning(Wms111);

        var result = await QueryAsync(
            http,
            Account(authMode: OgcServiceProvider.AuthQueryToken),
            Credentials((CredentialSlots.QueryToken, "query-secret")));

        Assert.True(result.IsSuccess);
        Assert.Contains("token=query-secret", Assert.Single(http.RequestUrls));
    }

    [Fact]
    public async Task HttpBaseUrl_WithoutConfirmation_IsRejectedBeforeSend()
    {
        var http = FakeHttpRequestService.Returning(Wms111);
        var account = Account(
            capabilitiesUrlOverride: "http://gis.example.test/geoserver/wms?request=GetCapabilities");

        var result = await QueryAsync(http, account);

        Assert.False(result.IsSuccess);
        Assert.Equal(BalanceErrorKind.ProtocolViolation, result.Error!.Kind);
        Assert.Empty(http.RequestUrls);
    }
}
