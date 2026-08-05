using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// SuperMap iServer 自托管健康探测（v0.9.0）。
/// 官方文档：support.supermap.com.cn（iServer REST API：services 目录 /
/// manager/serverstatus.json 管理接口）。
/// 基础探测：GET {baseUrl}/iserver/services.json（仅服务目录，无副作用）。
/// 可选管理状态探测默认关闭；只有用户主动开启并提供有权限凭据时才调用
/// {baseUrl}/iserver/manager/serverstatus.json。
/// 服务目录内容绝不完整写入日志；内网地址绝不进入通知。
/// </summary>
public sealed class SuperMapIServerProvider : IApiBalanceProvider
{
    public const string ProviderId = "supermap-iserver";
    public const string DisplayName = "SuperMap iServer";

    public const string BaseUrlField = "baseUrl";
    public const string ExpectedServiceField = "expectedService";
    public const string AllowHttpField = "allowHttp";
    public const string EnableManagerStatusField = "enableManagerStatus";

    private const string CatalogPath = "/iserver/services.json";
    private const string ManagerStatusPath = "/iserver/manager/serverstatus.json";

    private readonly SelfHostedHttpClient _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.SuperMapDescription"),
        SupportsAccountBalance: false,
        SupportsKeyQuota: false,
        SupportedMetricKinds: new[] { BalanceMetricKind.Other },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                "token",
                "Token / API Key",
                L10n.Get("Provider.SuperMapTokenHint"),
                IsDefault: true),
        },
        ApiKeyInputHint: L10n.Get("Provider.SuperMapTokenInputHint"),
        HelpUrl: "https://www.supermap.com/",
        SupportsTestConnection: true,
        DefaultBaseUrl: string.Empty,
        ConfigFields: new[]
        {
            new ProviderConfigField(
                BaseUrlField,
                "Provider.SuperMapBaseUrlLabel",
                "Provider.SuperMapBaseUrlHint",
                IsRequired: true,
                PlaceholderKey: "Provider.SuperMapBaseUrlPlaceholder"),
            new ProviderConfigField(
                ExpectedServiceField,
                "Provider.SuperMapExpectedServiceLabel",
                "Provider.SuperMapExpectedServiceHint",
                IsRequired: false),
            new ProviderConfigField(
                AllowHttpField,
                "Provider.SuperMapAllowHttpLabel",
                "Provider.SuperMapAllowHttpHint",
                IsRequired: false,
                Kind: ProviderConfigFieldKind.Boolean),
            new ProviderConfigField(
                EnableManagerStatusField,
                "Provider.SuperMapManagerStatusLabel",
                "Provider.SuperMapManagerStatusHint",
                IsRequired: false,
                Kind: ProviderConfigFieldKind.Boolean),
        },
        PrimaryMetricId: "supermap-iserver:service.availability",
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
                CredentialSlots.QueryToken,
                "Provider.SuperMapTokenLabel",
                "Provider.SuperMapTokenHint",
                IsRequired: false),
        },
        DetailedMetricKinds: new[]
        {
            MetricKind.ServiceAvailability,
            MetricKind.LatencyMilliseconds,
            MetricKind.ServiceCount,
        },
        ProbeConsumesQuota: false,
        ProbeDescriptionKey: "Provider.SuperMapProbeDescription");

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public SuperMapIServerProvider(IHttpRequestService http, AppLog? log = null)
    {
        _http = new SelfHostedHttpClient(http);
        _log = log;
    }

    public async Task<BalanceQueryResult> QueryBalanceAsync(
        ApiAccount account,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken)
    {
        string? baseUrl = account.ProviderConfig.TryGetValue(BaseUrlField, out var raw)
            ? raw?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.ConfigurationMissing,
                L10n.Get("Provider.ErrorMissingBaseUrl"));
        }

        bool allowHttp = ReadBool(account, AllowHttpField, defaultValue: false);
        bool enableManager = ReadBool(account, EnableManagerStatusField, defaultValue: false);
        string? expectedService = account.ProviderConfig.TryGetValue(ExpectedServiceField, out var expected)
            ? expected?.Trim()
            : null;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            Uri catalogUri = new(baseUrl.TrimEnd('/') + CatalogPath);
            using var response = await _http.SendAsync(
                BuildRequest(catalogUri, credentials),
                allowHttp,
                cancellationToken).ConfigureAwait(false);
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
                return FailureWithCode(BalanceErrorKind.CredentialInvalid, "Provider.Error401SuperMap", response);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return FailureWithCode(BalanceErrorKind.PermissionDenied, "Provider.Error403SuperMap", response);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return FailureWithCode(BalanceErrorKind.NotFound, "Provider.Error404SuperMap", response);
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

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return BalanceQueryResult.Failure(
                    BalanceErrorKind.EmptyContent,
                    L10n.Get("Provider.ErrorEmptyContent"));
            }

            using JsonDocument? document = ParseJson(body);
            if (document is null)
            {
                return BalanceQueryResult.Failure(
                    BalanceErrorKind.InvalidJson,
                    L10n.Get("Provider.ErrorInvalidJson"));
            }

            var catalog = ParseCatalog(document.RootElement);

            GeospatialStatus serverStatus = GeospatialStatus.Unknown;
            if (enableManager)
            {
                serverStatus = await ProbeManagerStatusAsync(
                    baseUrl,
                    credentials,
                    allowHttp,
                    cancellationToken).ConfigureAwait(false);
            }

            var metrics = BuildMetrics(
                account.ProviderId,
                catalog.Count,
                expectedService,
                catalog.ServiceNames,
                latency,
                serverStatus,
                enableManager);

            return BalanceQueryResult.Success(new BalanceSnapshot
            {
                SnapshotId = Guid.NewGuid().ToString("N"),
                AccountId = account.AccountId,
                ProviderId = account.ProviderId,
                IsAvailable = true,
                RetrievedAt = DateTimeOffset.UtcNow,
                Metrics = metrics,
            });
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
                L10n.Get("Provider.ErrorNetworkSuperMap"));
        }
        catch (UriFormatException)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.ProtocolViolation,
                L10n.Get("Provider.ErrorInvalidBaseUrl"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"SuperMap iServer 探测发生意外错误: {ex.GetType().Name}");
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Get("Provider.ErrorUnexpected"));
        }
    }

    private async Task<GeospatialStatus> ProbeManagerStatusAsync(
        string baseUrl,
        IReadOnlyDictionary<string, string> credentials,
        bool allowHttp,
        CancellationToken cancellationToken)
    {
        try
        {
            Uri uri = new(baseUrl.TrimEnd('/') + ManagerStatusPath);
            using var response = await _http.SendAsync(
                BuildRequest(uri, credentials),
                allowHttp,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return GeospatialStatus.ProviderError;
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument? document = ParseJson(body);
            return document is null ? GeospatialStatus.ProviderError : GeospatialStatus.Healthy;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"SuperMap 管理状态探测失败: {ex.GetType().Name}");
            return GeospatialStatus.ProviderError;
        }
    }

    private static HttpRequestMessage BuildRequest(
        Uri uri,
        IReadOnlyDictionary<string, string> credentials)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        if (credentials.TryGetValue(CredentialSlots.QueryToken, out var token)
            && !string.IsNullOrWhiteSpace(token))
        {
            // SuperMap iServer 服务目录支持 token 查询参数鉴权。
            string separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
            request.RequestUri = new Uri(uri.AbsoluteUri + separator + "token=" + Uri.EscapeDataString(token.Trim()));
        }

        return request;
    }

    private static (int Count, IReadOnlyList<string> ServiceNames) ParseCatalog(JsonElement root)
    {
        var names = new List<string>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("services", out JsonElement services)
            && services.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in services.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (item.TryGetProperty("name", out JsonElement name)
                    && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    names.Add(name.GetString()!.Trim());
                }
            }
        }

        return (names.Count, names);
    }

    private static IReadOnlyList<BalanceMetric> BuildMetrics(
        string providerId,
        int serviceCount,
        string? expectedService,
        IReadOnlyList<string> serviceNames,
        long latency,
        GeospatialStatus serverStatus,
        bool enableManager)
    {
        var metrics = new List<BalanceMetric>
        {
            GeospatialMetricFactory.ServiceAvailability(providerId, GeospatialStatus.Healthy),
            GeospatialMetricFactory.Latency(providerId, latency),
            GeospatialMetricFactory.CountMetric(
                providerId,
                "services.count",
                MetricKind.ServiceCount,
                serviceCount),
        };

        if (!string.IsNullOrWhiteSpace(expectedService))
        {
            bool present = serviceNames.Any(name =>
                string.Equals(name, expectedService, StringComparison.OrdinalIgnoreCase));
            metrics.Add(GeospatialMetricFactory.BooleanMetric(
                providerId,
                "expected-service.present",
                "Metric.ExpectedServiceName",
                present));
        }

        if (enableManager)
        {
            metrics.Add(GeospatialMetricFactory.StatusMetric(
                providerId,
                "server.status",
                MetricKind.ServiceAvailability,
                serverStatus));
        }

        return metrics;
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

    private static JsonDocument? ParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
