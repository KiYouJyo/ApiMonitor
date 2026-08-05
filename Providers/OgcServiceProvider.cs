using System.Diagnostics;
using System.Net;
using System.Text;
using System.Xml;
using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// 通用 OGC 服务健康探测（v0.9.0）：WMS 1.1.1/1.3.0、WMTS 1.0.0、
/// WFS 1.0.0/2.0.0（含 1.1.0）。适用于 MapGIS Server、SuperMap iServer、
/// GeoServer、ArcGIS Server 等发布的 OGC 服务。
/// 默认探测只调用 GetCapabilities，绝不使用 GetMap / GetFeature / 空间查询。
/// XML 解析强制安全：禁用 DTD/外部实体/实体扩展/远程 Schema，限制大小与深度，
/// 非 XML 响应安全失败（InvalidXml）。
/// </summary>
public sealed class OgcServiceProvider : IApiBalanceProvider
{
    public const string ProviderId = "ogc-service";
    public const string DisplayName = "OGC 服务";

    public const string ServiceTypeField = "serviceType";
    public const string CapabilitiesUrlField = "capabilitiesUrl";
    public const string ExpectedLayerField = "expectedLayer";
    public const string AuthModeField = "authMode";
    public const string AllowHttpField = "allowHttp";

    public const string AuthNone = "none";
    public const string AuthBasic = "basic";
    public const string AuthBearer = "bearer";
    public const string AuthQueryToken = "query-token";

    private readonly SelfHostedHttpClient _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.OgcDescription"),
        SupportsAccountBalance: false,
        SupportsKeyQuota: false,
        SupportedMetricKinds: new[] { BalanceMetricKind.Other },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                "ogc",
                "OGC 服务",
                L10n.Get("Provider.OgcKeyHint"),
                IsDefault: true),
        },
        ApiKeyInputHint: L10n.Get("Provider.OgcKeyInputHint"),
        HelpUrl: "https://www.ogc.org/standards/",
        SupportsTestConnection: true,
        DefaultBaseUrl: string.Empty,
        ConfigFields: new[]
        {
            new ProviderConfigField(
                ServiceTypeField,
                "Provider.OgcServiceTypeLabel",
                "Provider.OgcServiceTypeHint",
                IsRequired: true,
                PlaceholderKey: "Provider.OgcServiceTypePlaceholder"),
            new ProviderConfigField(
                CapabilitiesUrlField,
                "Provider.OgcCapabilitiesUrlLabel",
                "Provider.OgcCapabilitiesUrlHint",
                IsRequired: true,
                PlaceholderKey: "Provider.OgcCapabilitiesUrlPlaceholder"),
            new ProviderConfigField(
                ExpectedLayerField,
                "Provider.OgcExpectedLayerLabel",
                "Provider.OgcExpectedLayerHint",
                IsRequired: false),
            new ProviderConfigField(
                AuthModeField,
                "Provider.OgcAuthModeLabel",
                "Provider.OgcAuthModeHint",
                IsRequired: false,
                PlaceholderKey: "Provider.OgcAuthModePlaceholder"),
            new ProviderConfigField(
                AllowHttpField,
                "Provider.OgcAllowHttpLabel",
                "Provider.OgcAllowHttpHint",
                IsRequired: false,
                Kind: ProviderConfigFieldKind.Boolean),
        },
        PrimaryMetricId: "ogc-service:service.availability",
        Currency: null,
        SupportsMultiCurrency: false,
        SupportsBreakdown: false,
        SupportsCredentialValidation: false,
        AllowCustomEndpoint: true,
        Category: ProviderCategory.GisServer,
        Capabilities: new[]
        {
            ProviderCapability.HealthProbe,
            ProviderCapability.CredentialValidation,
            ProviderCapability.PermissionValidation,
            ProviderCapability.ServiceCatalog,
            ProviderCapability.LatencyMeasurement,
        },
        CredentialSlots: new[]
        {
            new ProviderCredentialSlot(
                CredentialSlots.Username,
                "Provider.OgcUsernameLabel",
                "Provider.OgcUsernameHint",
                IsRequired: true,
                IsSecret: false,
                ConditionalOnConfigFieldId: AuthModeField,
                ConditionalOnConfigValue: AuthBasic),
            new ProviderCredentialSlot(
                CredentialSlots.Password,
                "Provider.OgcPasswordLabel",
                "Provider.OgcPasswordHint",
                IsRequired: true,
                ConditionalOnConfigFieldId: AuthModeField,
                ConditionalOnConfigValue: AuthBasic),
            new ProviderCredentialSlot(
                CredentialSlots.BearerToken,
                "Provider.OgcBearerLabel",
                "Provider.OgcBearerHint",
                IsRequired: true,
                ConditionalOnConfigFieldId: AuthModeField,
                ConditionalOnConfigValue: AuthBearer),
            new ProviderCredentialSlot(
                CredentialSlots.QueryToken,
                "Provider.OgcQueryTokenLabel",
                "Provider.OgcQueryTokenHint",
                IsRequired: true,
                ConditionalOnConfigFieldId: AuthModeField,
                ConditionalOnConfigValue: AuthQueryToken),
        },
        DetailedMetricKinds: new[]
        {
            MetricKind.ServiceAvailability,
            MetricKind.LatencyMilliseconds,
            MetricKind.LayerCount,
        },
        ProbeConsumesQuota: false,
        ProbeDescriptionKey: "Provider.OgcProbeDescription");

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public OgcServiceProvider(IHttpRequestService http, AppLog? log = null)
    {
        _http = new SelfHostedHttpClient(http);
        _log = log;
    }

    public async Task<BalanceQueryResult> QueryBalanceAsync(
        ApiAccount account,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken)
    {
        string? capabilitiesUrl = account.ProviderConfig.TryGetValue(CapabilitiesUrlField, out var raw)
            ? raw?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(capabilitiesUrl))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.ConfigurationMissing,
                L10n.Get("Provider.ErrorMissingCapabilitiesUrl"));
        }

        string authMode = account.ProviderConfig.TryGetValue(AuthModeField, out var mode)
            ? mode?.Trim().ToLowerInvariant() ?? AuthNone
            : AuthNone;
        bool allowHttp = ReadBool(account, AllowHttpField, defaultValue: false);
        string? expectedLayer = account.ProviderConfig.TryGetValue(ExpectedLayerField, out var expected)
            ? expected?.Trim()
            : null;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            Uri uri = new(capabilitiesUrl);
            var request = BuildRequest(uri, authMode, credentials);
            using var response = await _http.SendAsync(request, allowHttp, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            long latency = stopwatch.ElapsedMilliseconds;

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                return BalanceQueryResult.Failure(
                    BalanceErrorKind.RedirectBlocked,
                    L10n.Get("Provider.ErrorRedirectBlocked"),
                    httpStatusCode: (int)response.StatusCode);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return FailureWithCode(BalanceErrorKind.CredentialInvalid, "Provider.Error401Ogc", response);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return FailureWithCode(BalanceErrorKind.PermissionDenied, "Provider.Error403Ogc", response);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return FailureWithCode(BalanceErrorKind.NotFound, "Provider.Error404Ogc", response);
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                return FailureWithCode(BalanceErrorKind.RateLimited, "Provider.Error429", response);
            }

            if ((int)response.StatusCode >= 500)
            {
                return FailureWithCode(
                    BalanceErrorKind.ServerError,
                    "Provider.ErrorServiceUnavailable",
                    response);
            }

            if (!response.IsSuccessStatusCode)
            {
                return FailureWithCode(BalanceErrorKind.Unknown, "Provider.ErrorUnexpectedStatus", response);
            }

            if (response.Content.Headers.ContentLength is { } length
                && length > SecureXml.MaxDocumentBytes)
            {
                return BalanceQueryResult.Failure(
                    BalanceErrorKind.TooLarge,
                    L10n.Get("Provider.ErrorXmlTooLarge"));
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Length > SecureXml.MaxDocumentBytes)
            {
                return BalanceQueryResult.Failure(
                    BalanceErrorKind.TooLarge,
                    L10n.Get("Provider.ErrorXmlTooLarge"));
            }

            OgcParseResult parsed;
            try
            {
                parsed = ParseCapabilities(body);
            }
            catch (XmlException)
            {
                return BalanceQueryResult.Failure(
                    BalanceErrorKind.InvalidXml,
                    L10n.Get("Provider.ErrorInvalidXml"));
            }

            if (parsed.IsErrorReport)
            {
                return BalanceQueryResult.Success(BuildSnapshot(
                    account,
                    GeospatialStatus.InvalidResponse,
                    latency,
                    parsed.ServiceType,
                    parsed.ServiceVersion,
                    parsed.LayerCount,
                    expectedLayer,
                    parsed.LayerNames));
            }

            return BalanceQueryResult.Success(BuildSnapshot(
                account,
                parsed.ServiceType is null ? GeospatialStatus.InvalidResponse : GeospatialStatus.Healthy,
                latency,
                parsed.ServiceType,
                parsed.ServiceVersion,
                parsed.LayerCount,
                expectedLayer,
                parsed.LayerNames));
        }
        catch (SelfHostedRequestException ex)
        {
            return BalanceQueryResult.Failure(ex.Kind, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Timeout,
                L10n.Get("Provider.ErrorTimeout"));
        }
        catch (HttpRequestException)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Network,
                L10n.Get("Provider.ErrorNetworkOgc"));
        }
        catch (UriFormatException)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.ProtocolViolation,
                L10n.Get("Provider.ErrorInvalidCapabilitiesUrl"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"OGC 服务探测发生意外错误: {ex.GetType().Name}");
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Get("Provider.ErrorUnexpected"));
        }
    }

    private static HttpRequestMessage BuildRequest(
        Uri uri,
        string authMode,
        IReadOnlyDictionary<string, string> credentials)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/xml");
        request.Headers.Accept.ParseAdd("text/xml");

        switch (authMode)
        {
            case AuthBasic:
                string username = credentials.TryGetValue(CredentialSlots.Username, out var user)
                    ? user ?? string.Empty
                    : string.Empty;
                string password = credentials.TryGetValue(CredentialSlots.Password, out var pass)
                    ? pass ?? string.Empty
                    : string.Empty;
                string token = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(username + ":" + password));
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", token);
                break;
            case AuthBearer:
                if (credentials.TryGetValue(CredentialSlots.BearerToken, out var bearer)
                    && !string.IsNullOrWhiteSpace(bearer))
                {
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer.Trim());
                }

                break;
            case AuthQueryToken:
                if (credentials.TryGetValue(CredentialSlots.QueryToken, out var queryToken)
                    && !string.IsNullOrWhiteSpace(queryToken))
                {
                    string separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
                    request.RequestUri = new Uri(
                        uri.AbsoluteUri + separator + "token=" + Uri.EscapeDataString(queryToken.Trim()));
                }

                break;
        }

        return request;
    }

    /// <summary>
    /// 安全解析 GetCapabilities：
    ///   WMS 1.1.1：根 WMT_MS_Capabilities；错误 ServiceExceptionReport/ServiceException；
    ///   WMS 1.3.0：根 WMS_Capabilities；
    ///   WMTS 1.0.0：根 Capabilities（wmts 命名空间），图层在 Contents/Layer；
    ///   WFS 1.0.0/1.1.0/2.0.0：根 WFS_Capabilities，要素类型在 FeatureTypeList/FeatureType；
    ///   OWS 错误：ExceptionReport/Exception。
    /// </summary>
    internal static OgcParseResult ParseCapabilities(string xml)
    {
        using var reader = SecureXml.CreateSafeReader(xml);
        var doc = new XmlDocument { XmlResolver = null };
        try
        {
            doc.Load(reader);
        }
        catch (XmlException)
        {
            throw;
        }

        if (MaxDepth(doc, 0) > SecureXml.MaxDepth)
        {
            throw new XmlException("XML depth exceeds the configured limit.");
        }

        XmlElement? root = doc.DocumentElement;
        if (root is null)
        {
            throw new XmlException("No root element.");
        }

        string rootName = root.LocalName;
        string rootNamespace = root.NamespaceURI;
        string? version = root.GetAttribute("version");

        if (rootName == "ServiceExceptionReport")
        {
            return new OgcParseResult(IsErrorReport: true, null, version, 0, Array.Empty<string>());
        }

        if (rootName == "ExceptionReport")
        {
            return new OgcParseResult(IsErrorReport: true, null, version, 0, Array.Empty<string>());
        }

        if (rootName == "WMT_MS_Capabilities")
        {
            if (version is not null && version != "1.1.1")
            {
                return new OgcParseResult(false, null, version, 0, Array.Empty<string>());
            }

            var layers = CollectNamedChildren(root, "Layer", "Name");
            return new OgcParseResult(false, "WMS", version ?? "1.1.1", layers.Count, layers);
        }

        if (rootName == "WMS_Capabilities")
        {
            if (version is not null && version != "1.3.0" && version != "1.1.1")
            {
                return new OgcParseResult(false, null, version, 0, Array.Empty<string>());
            }

            var layers = CollectNamedChildren(root, "Layer", "Name");
            return new OgcParseResult(false, "WMS", version ?? "1.3.0", layers.Count, layers);
        }

        if (rootName == "Capabilities"
            && rootNamespace.Contains("wmts", StringComparison.OrdinalIgnoreCase))
        {
            if (version is not null && version != "1.0.0")
            {
                return new OgcParseResult(false, null, version, 0, Array.Empty<string>());
            }

            var layers = CollectNamedChildren(root, "Layer", "Identifier");
            return new OgcParseResult(false, "WMTS", version ?? "1.0.0", layers.Count, layers);
        }

        if (rootName == "WFS_Capabilities")
        {
            if (version is not null
                && version is not ("1.0.0" or "1.1.0" or "2.0.0"))
            {
                return new OgcParseResult(false, null, version, 0, Array.Empty<string>());
            }

            var types = CollectNamedChildren(root, "FeatureType", "Name");
            return new OgcParseResult(false, "WFS", version ?? "1.0.0", types.Count, types);
        }

        // 无法识别的根元素：视为结构变化，安全失败。
        return new OgcParseResult(false, null, version, 0, Array.Empty<string>());
    }

    /// <summary>
    /// 收集所有 <paramref name="containerLocalName"/> 元素中直接子元素为
    /// <paramref name="childLocalName"/> 的名称（命名空间无关）。
    /// </summary>
    private static IReadOnlyList<string> CollectNamedChildren(
        XmlElement root,
        string containerLocalName,
        string childLocalName)
    {
        var names = new List<string>();
        foreach (XmlNode node in root.SelectNodes($"//*[local-name()='{containerLocalName}']")!)
        {
            if (node is not XmlElement container)
            {
                continue;
            }

            foreach (XmlNode child in container.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element
                    && child.LocalName == childLocalName
                    && !string.IsNullOrWhiteSpace(child.InnerText.Trim()))
                {
                    names.Add(child.InnerText.Trim());
                    break;
                }
            }
        }

        return names;
    }

    private static int MaxDepth(XmlNode node, int depth)
    {
        int max = depth;
        foreach (XmlNode child in node.ChildNodes)
        {
            max = Math.Max(max, MaxDepth(child, depth + 1));
        }

        return max;
    }

    private static BalanceSnapshot BuildSnapshot(
        ApiAccount account,
        GeospatialStatus status,
        long latency,
        string? serviceType,
        string? serviceVersion,
        int layerCount,
        string? expectedLayer,
        IReadOnlyList<string> layerNames)
    {
        var metrics = new List<BalanceMetric>
        {
            GeospatialMetricFactory.ServiceAvailability(account.ProviderId, status),
            GeospatialMetricFactory.Latency(account.ProviderId, latency),
        };

        if (!string.IsNullOrWhiteSpace(serviceType))
        {
            metrics.Add(GeospatialMetricFactory.TypeMetric(
                account.ProviderId,
                "service.type",
                serviceType!));
        }

        if (!string.IsNullOrWhiteSpace(serviceVersion))
        {
            metrics.Add(GeospatialMetricFactory.VersionMetric(
                account.ProviderId,
                "service.version",
                serviceVersion!));
        }

        metrics.Add(GeospatialMetricFactory.CountMetric(
            account.ProviderId,
            "layers.count",
            MetricKind.LayerCount,
            layerCount));

        if (!string.IsNullOrWhiteSpace(expectedLayer))
        {
            bool present = layerNames.Any(name =>
                string.Equals(name, expectedLayer, StringComparison.OrdinalIgnoreCase));
            metrics.Add(GeospatialMetricFactory.BooleanMetric(
                account.ProviderId,
                "expected-layer.present",
                "Metric.ExpectedLayerName",
                present));
        }

        return new BalanceSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            ProviderId = account.ProviderId,
            IsAvailable = status == GeospatialStatus.Healthy,
            RetrievedAt = DateTimeOffset.UtcNow,
            Metrics = metrics,
        };
    }

    private static bool ReadBool(ApiAccount account, string field, bool defaultValue)
    {
        if (account.ProviderConfig.TryGetValue(field, out var raw)
            && bool.TryParse(raw, out bool value))
        {
            return value;
        }

        return defaultValue;
    }

    private static BalanceQueryResult FailureWithCode(
        BalanceErrorKind kind,
        string messageKey,
        HttpResponseMessage response) =>
        BalanceQueryResult.Failure(
            kind,
            L10n.Get(messageKey),
            httpStatusCode: (int)response.StatusCode);
}

/// <summary>OGC GetCapabilities 解析结果（仅保留安全元数据，不含完整响应）。</summary>
public sealed record OgcParseResult(
    bool IsErrorReport,
    string? ServiceType,
    string? ServiceVersion,
    int LayerCount,
    IReadOnlyList<string> LayerNames);
