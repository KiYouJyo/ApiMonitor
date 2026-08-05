using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// 高德开放平台 Web 服务 API 健康探测（v0.9.0）。
/// 官方文档（2022-10-12 更新）：https://lbs.amap.com/api/webservice/guide/tools/info
/// 探测接口：GET https://restapi.amap.com/v3/geocode/geo
///   address=北京市东城区天安门广场&city=北京&output=json&key=&lt;KEY&gt;
/// 固定公开地址，绝不携带用户位置/项目地址/历史查询。
/// 一次探测消耗一次调用额度（UI 明确提示）。
/// 成功判断：status=="1" 且 infocode=="10000"。
/// 高德没有公开配额查询 API：精确月配额/流量包/账户余额一律未知（null），
/// 不得把一次成功探测解释为“配额充足”，也不得抓取高德控制台。
/// </summary>
public sealed class AmapBalanceProvider : IApiBalanceProvider
{
    public const string ProviderId = "amap";
    public const string DisplayName = "高德开放平台";
    public const string OfficialHost = "restapi.amap.com";

    private const string ProbePath = "/v3/geocode/geo";
    private const string ProbeAddress = "北京市东城区天安门广场";
    private const string ProbeCity = "北京";

    private readonly ProviderHttpClient _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.AmapDescription"),
        SupportsAccountBalance: false,
        SupportsKeyQuota: false,
        SupportedMetricKinds: new[] { BalanceMetricKind.Other },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                "api-key",
                "Web 服务 API Key",
                L10n.Get("Provider.AmapKeyHint"),
                IsDefault: true),
        },
        ApiKeyInputHint: L10n.Get("Provider.AmapKeyInputHint"),
        HelpUrl: "https://console.amap.com/",
        SupportsTestConnection: true,
        DefaultBaseUrl: $"https://{OfficialHost}",
        ConfigFields: Array.Empty<ProviderConfigField>(),
        PrimaryMetricId: "amap:service.availability",
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
                L10n.Get("Provider.AmapKeyInputHint"),
                IsRequired: true),
            new ProviderCredentialSlot(
                CredentialSlots.Secret,
                "Provider.AmapSecretLabel",
                "Provider.AmapSecretHint",
                IsRequired: false),
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
        ProbeDescriptionKey: "Provider.AmapProbeDescription");

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public AmapBalanceProvider(IHttpRequestService http, AppLog? log = null)
    {
        // 高德仅允许 HTTPS 官方主机；429/QPS 超限不自动重试（v0.9.0 配额保护）。
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
        if (!credentials.TryGetValue(CredentialSlots.Primary, out var apiKey)
            || string.IsNullOrWhiteSpace(apiKey))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.MissingCredential,
                L10n.Get("Provider.ErrorNoKey"));
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.SendWithRetryAsync(
                () => BuildRequest(apiKey.Trim()),
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
                L10n.Get("Provider.ErrorNetworkAmap"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"高德探测发生意外错误: {ex.GetType().Name}");
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Get("Provider.ErrorUnexpected"));
        }
    }

    private static HttpRequestMessage BuildRequest(string apiKey)
    {
        string query = string.Join(
            "&",
            $"address={Uri.EscapeDataString(ProbeAddress)}",
            $"city={Uri.EscapeDataString(ProbeCity)}",
            "output=json",
            $"key={Uri.EscapeDataString(apiKey)}");
        return new HttpRequestMessage(
            HttpMethod.Get,
            $"https://{OfficialHost}{ProbePath}?{query}");
    }

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
            return FailureWithCode(BalanceErrorKind.CredentialInvalid, "Provider.Error401Amap", response);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return FailureWithCode(BalanceErrorKind.PermissionDenied, "Provider.Error403Amap", response);
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
            || !root.TryGetProperty("status", out JsonElement statusElement)
            || statusElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
        {
            // 响应缺少 status 字段：结构变化，安全失败。
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorUnsupportedBalanceFormat"));
        }

        string? statusText = statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString()
            : statusElement.GetRawText();
        string? infocode = ReadString(root, "infocode");
        if (!string.Equals(statusText?.Trim(), "1", StringComparison.Ordinal))
        {
            // status != "1"：按官方状态码表映射（禁止凭记忆补全）。
            var status = MapErrorCode(infocode);
            return BalanceQueryResult.Success(BuildSnapshot(
                account,
                status,
                latencyMilliseconds,
                errorMessage: ErrorMessageFor(status, infocode)));
        }

        if (!string.Equals(infocode, "10000", StringComparison.Ordinal))
        {
            // status=="1" 但 infocode 异常：按状态码表映射。
            var status = MapErrorCode(infocode);
            return BalanceQueryResult.Success(BuildSnapshot(
                account,
                status,
                latencyMilliseconds,
                errorMessage: ErrorMessageFor(status, infocode)));
        }

        return BalanceQueryResult.Success(BuildSnapshot(
            account,
            GeospatialStatus.Healthy,
            latencyMilliseconds,
            errorMessage: null));
    }

    /// <summary>
    /// 高德官方状态码映射（文档：lbs.amap.com/api/webservice/guide/tools/info）。
    /// 20000 系参数错误、300xx 引擎错误、未收录状态码一律映射为安全分类，
    /// 保留官方数值码供界面显示，不猜测语义。
    /// </summary>
    private static GeospatialStatus MapErrorCode(string? infocode) =>
        infocode switch
        {
            "10000" => GeospatialStatus.Healthy,
            "10001" => GeospatialStatus.CredentialInvalid,
            "10002" => GeospatialStatus.ServiceNotEnabled,
            "10003" => GeospatialStatus.QuotaExceeded,
            "10004" => GeospatialStatus.RateLimited,
            "10005" => GeospatialStatus.IpWhitelistDenied,
            "10006" => GeospatialStatus.RefererDomainDenied,
            "10007" => GeospatialStatus.SignatureInvalid,
            "10008" => GeospatialStatus.CredentialInvalid,
            "10009" => GeospatialStatus.KeyTypeMismatch,
            "10010" => GeospatialStatus.QuotaExceeded,
            "10012" => GeospatialStatus.PermissionDenied,
            "10013" => GeospatialStatus.CredentialInvalid,
            "10014" or "10019" or "10020" or "10021" => GeospatialStatus.RateLimited,
            "10016" => GeospatialStatus.ProviderError,
            "40000" => GeospatialStatus.QuotaExceeded,
            "40002" => GeospatialStatus.ServiceNotEnabled,
            _ when infocode?.StartsWith("2", StringComparison.Ordinal) == true => GeospatialStatus.InvalidResponse,
            _ when infocode?.StartsWith("3", StringComparison.Ordinal) == true => GeospatialStatus.ProviderError,
            _ => GeospatialStatus.ProviderError,
        };

    private static BalanceSnapshot BuildSnapshot(
        ApiAccount account,
        GeospatialStatus status,
        long latencyMilliseconds,
        string? errorMessage) =>
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

    private static string? ErrorMessageFor(GeospatialStatus status, string? infocode) =>
        status == GeospatialStatus.Healthy
            ? null
            : string.IsNullOrWhiteSpace(infocode)
                ? GeospatialMetricFactory.StatusText(status)
                : $"{GeospatialMetricFactory.StatusText(status)} ({infocode})";

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

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement element)
            && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
}
