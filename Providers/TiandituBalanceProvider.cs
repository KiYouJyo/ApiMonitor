using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// 国家地理信息公共服务平台天地图地名搜索 V2.0 健康探测（v0.9.0）。
/// 官方文档与返回信息码表：http://lbs.tianditu.gov.cn/server/search2.html
/// 探测接口：GET https://api.tianditu.gov.cn/v2/search
///   postStr=&lt;URL 编码的固定 JSON&gt;&amp;type=query&amp;tk=&lt;TOKEN&gt;
/// 固定最小返回量参数（count=1，queryType=1 普通搜索），查询 JSON 必须正确 URL 编码。
/// 官方未公开 Token 无效/权限不足/调用超限的状态码，因此未知 infocode 一律映射为
/// ProviderError 并保留数值码，不猜测语义（宁可显示“未知状态码”，不伪造结论）。
/// 正常状态：status.infocode==1000；3001 表示服务正常但没有结果。
/// </summary>
public sealed class TiandituBalanceProvider : IApiBalanceProvider
{
    public const string ProviderId = "tianditu";
    public const string DisplayName = "天地图";
    public const string OfficialHost = "api.tianditu.gov.cn";

    private const string ProbePath = "/v2/search";

    private static readonly string ProbePostJson = JsonSerializer.Serialize(new
    {
        keyWord = "北京市",
        level = 10,
        mapBound = "115.7,39.4,117.4,41.1",
        queryType = 1,
        start = 0,
        count = 1,
    });

    private readonly ProviderHttpClient _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.TiandituDescription"),
        SupportsAccountBalance: false,
        SupportsKeyQuota: false,
        SupportedMetricKinds: new[] { BalanceMetricKind.Other },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                "token",
                "服务 Token（tk）",
                L10n.Get("Provider.TiandituKeyHint"),
                IsDefault: true),
        },
        ApiKeyInputHint: L10n.Get("Provider.TiandituKeyInputHint"),
        HelpUrl: "http://lbs.tianditu.gov.cn/",
        SupportsTestConnection: true,
        DefaultBaseUrl: $"https://{OfficialHost}",
        ConfigFields: Array.Empty<ProviderConfigField>(),
        PrimaryMetricId: "tianditu:service.availability",
        Currency: null,
        SupportsMultiCurrency: false,
        SupportsBreakdown: false,
        SupportsCredentialValidation: true,
        AllowCustomEndpoint: false,
        Category: ProviderCategory.Geospatial,
        Capabilities: new[]
        {
            ProviderCapability.HealthProbe,
            ProviderCapability.CredentialValidation,
            ProviderCapability.PermissionValidation,
            ProviderCapability.QuotaStateDetection,
            ProviderCapability.LatencyMeasurement,
        },
        CredentialSlots: new[]
        {
            new ProviderCredentialSlot(
                CredentialSlots.Primary,
                "Dialog.ApiKey.Header",
                L10n.Get("Provider.TiandituKeyInputHint"),
                IsRequired: true),
        },
        DetailedMetricKinds: new[]
        {
            MetricKind.ServiceAvailability,
            MetricKind.LatencyMilliseconds,
            MetricKind.CredentialStatus,
            MetricKind.PermissionStatus,
            MetricKind.QuotaState,
        },
        ProbeConsumesQuota: true,
        ProbeDescriptionKey: "Provider.TiandituProbeDescription");

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public TiandituBalanceProvider(IHttpRequestService http, AppLog? log = null)
    {
        _http = new ProviderHttpClient(
            http,
            new[] { OfficialHost },
            retryOnRateLimit: false);
        _log = log;
    }

    public async Task<BalanceQueryResult> QueryBalanceAsync(
        ApiAccount account,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken)
    {
        if (!credentials.TryGetValue(CredentialSlots.Primary, out var token)
            || string.IsNullOrWhiteSpace(token))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.MissingCredential,
                L10n.Get("Provider.ErrorNoKey"));
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.SendWithRetryAsync(
                () => BuildRequest(token.Trim()),
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return await HandleResponseAsync(account, response, stopwatch.ElapsedMilliseconds, cancellationToken)
                .ConfigureAwait(false);
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
                L10n.Get("Provider.ErrorNetworkTianditu"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"天地图探测发生意外错误: {ex.GetType().Name}");
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Get("Provider.ErrorUnexpected"));
        }
    }

    internal static string BuildProbeUrl(string token) =>
        $"https://{OfficialHost}{ProbePath}?postStr={Uri.EscapeDataString(ProbePostJson)}&type=query&tk={Uri.EscapeDataString(token)}";

    private static HttpRequestMessage BuildRequest(string token) =>
        new(HttpMethod.Get, BuildProbeUrl(token));

    private static async Task<BalanceQueryResult> HandleResponseAsync(
        ApiAccount account,
        HttpResponseMessage response,
        long latencyMilliseconds,
        CancellationToken cancellationToken)
    {
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.RedirectBlocked,
                L10n.Get("Provider.ErrorRedirectBlocked"),
                httpStatusCode: (int)response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return FailureWithCode(BalanceErrorKind.CredentialInvalid, "Provider.Error401Tianditu", response);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return FailureWithCode(BalanceErrorKind.PermissionDenied, "Provider.Error403Tianditu", response);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return FailureWithCode(BalanceErrorKind.NotFound, "Provider.Error404", response);
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

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("status", out JsonElement status)
            || status.ValueKind != JsonValueKind.Object
            || !status.TryGetProperty("infocode", out JsonElement infocodeElement)
            || !infocodeElement.TryGetInt32(out int infocode))
        {
            // 缺少 status / infocode：响应结构变化，安全失败。
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorUnsupportedBalanceFormat"));
        }

        var mapped = MapStatus(infocode);
        return BalanceQueryResult.Success(BuildSnapshot(
            account,
            mapped,
            latencyMilliseconds));
    }

    /// <summary>
    /// 天地图官方返回信息码表（lbs.tianditu.gov.cn/server/search2.html 2.1 节）：
    /// 1000=OK、2001–2007=参数错误、3000=服务器出错、3001=没有找到数据。
    /// 官方未公开 Token 无效等状态码：未知码一律 ProviderError，不猜测。
    /// </summary>
    private static GeospatialStatus MapStatus(int infocode) =>
        infocode switch
        {
            1000 => GeospatialStatus.Healthy,
            2001 or 2002 or 2004 or 2005 or 2006 or 2007 => GeospatialStatus.InvalidResponse,
            2003 => GeospatialStatus.ConfigurationMissing,
            3000 => GeospatialStatus.ProviderError,
            3001 => GeospatialStatus.Healthy,
            _ => GeospatialStatus.ProviderError,
        };

    private static BalanceSnapshot BuildSnapshot(
        ApiAccount account,
        GeospatialStatus status,
        long latencyMilliseconds) =>
        new()
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            ProviderId = account.ProviderId,
            IsAvailable = status == GeospatialStatus.Healthy,
            RetrievedAt = DateTimeOffset.UtcNow,
            Metrics = GeospatialMetricFactory.BuildMapMetricSet(
                account.ProviderId,
                status,
                latencyMilliseconds),
        };

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
