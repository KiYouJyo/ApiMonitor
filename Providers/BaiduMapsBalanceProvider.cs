using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// 百度地图开放平台 Web API 健康探测（v0.9.0）。
/// 官方状态码表：https://lbs.baidu.com/faq/api?title=webapi/appendix
/// 探测接口：GET https://api.map.baidu.com/geocoding/v3/
///   address=北京市海淀区上地十街10号&output=json&ak=&lt;AK&gt;
/// 固定公开地址，不携带用户位置。一次探测消耗一次调用额度。
/// 成功判断：status==0。
/// AK 必填；SK 可选（SN 校验），存于 Credential Locker secret 槽位。
/// 百度没有公开配额查询 API：精确剩余量保持未知。
/// </summary>
public sealed class BaiduMapsBalanceProvider : IApiBalanceProvider
{
    public const string ProviderId = "baidu-maps";
    public const string DisplayName = "百度地图开放平台";
    public const string OfficialHost = "api.map.baidu.com";

    private const string ProbePath = "/geocoding/v3/";
    private const string ProbeAddress = "北京市海淀区上地十街10号";

    private readonly ProviderHttpClient _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.BaiduMapsDescription"),
        SupportsAccountBalance: false,
        SupportsKeyQuota: false,
        SupportedMetricKinds: new[] { BalanceMetricKind.Other },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                "ak",
                "服务端 AK",
                L10n.Get("Provider.BaiduMapsKeyHint"),
                IsDefault: true),
        },
        ApiKeyInputHint: L10n.Get("Provider.BaiduMapsKeyInputHint"),
        HelpUrl: "https://lbsyun.baidu.com/",
        SupportsTestConnection: true,
        DefaultBaseUrl: $"https://{OfficialHost}",
        ConfigFields: Array.Empty<ProviderConfigField>(),
        PrimaryMetricId: "baidu-maps:service.availability",
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
                L10n.Get("Provider.BaiduMapsKeyInputHint"),
                IsRequired: true),
            new ProviderCredentialSlot(
                CredentialSlots.Secret,
                "Provider.BaiduMapsSecretLabel",
                "Provider.BaiduMapsSecretHint",
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
        ProbeDescriptionKey: "Provider.BaiduMapsProbeDescription");

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public BaiduMapsBalanceProvider(IHttpRequestService http, AppLog? log = null)
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
        if (!credentials.TryGetValue(CredentialSlots.Primary, out var ak)
            || string.IsNullOrWhiteSpace(ak))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.MissingCredential,
                L10n.Get("Provider.ErrorNoKey"));
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.SendWithRetryAsync(
                () => BuildRequest(ak.Trim()),
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
                L10n.Get("Provider.ErrorNetworkBaiduMaps"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"百度地图探测发生意外错误: {ex.GetType().Name}");
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Get("Provider.ErrorUnexpected"));
        }
    }

    private static HttpRequestMessage BuildRequest(string ak)
    {
        string query = string.Join(
            "&",
            $"address={Uri.EscapeDataString(ProbeAddress)}",
            "output=json",
            $"ak={Uri.EscapeDataString(ak)}");
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
            return FailureWithCode(BalanceErrorKind.CredentialInvalid, "Provider.Error401BaiduMaps", response);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return FailureWithCode(BalanceErrorKind.PermissionDenied, "Provider.Error403BaiduMaps", response);
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
            || !statusElement.TryGetInt32(out int status))
        {
            // 返回结构变化：安全失败。
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorUnsupportedBalanceFormat"));
        }

        var mapped = MapStatus(status);
        return BalanceQueryResult.Success(BuildSnapshot(
            account,
            mapped,
            latencyMilliseconds));
    }

    /// <summary>
    /// 百度地图官方状态码映射（lbs.baidu.com/faq/api?title=webapi/appendix）。
    /// 2xx=权限错误、3xx=配额错误；未收录状态码一律 ProviderError（保留数值码）。
    /// </summary>
    private static GeospatialStatus MapStatus(int status) =>
        status switch
        {
            0 => GeospatialStatus.Healthy,
            1 => GeospatialStatus.ProviderError,
            2 => GeospatialStatus.InvalidResponse,
            3 => GeospatialStatus.PermissionDenied,
            4 => GeospatialStatus.QuotaExceeded,
            5 => GeospatialStatus.CredentialInvalid,
            101 => GeospatialStatus.ConfigurationMissing,
            102 => GeospatialStatus.IpWhitelistDenied,
            210 => GeospatialStatus.IpWhitelistDenied,
            211 => GeospatialStatus.SignatureInvalid,
            240 => GeospatialStatus.ServiceNotEnabled,
            302 => GeospatialStatus.QuotaExceeded,
            401 => GeospatialStatus.RateLimited,
            >= 200 and < 300 => GeospatialStatus.PermissionDenied,
            >= 300 and < 400 => GeospatialStatus.QuotaExceeded,
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
