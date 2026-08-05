using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// 腾讯位置服务 WebService API 健康探测（v0.9.0）。
/// 官方状态码表：https://lbs.qq.com/service/webService/webServiceGuide/status
/// 行政区划列表接口（官方文档：webServiceGuide/search/webServiceDistrict）：
///   GET https://apis.map.qq.com/ws/district/v1/list?key=&lt;KEY&gt;&amp;output=json
/// 选择依据：比地理编码更低副作用、官方稳定、固定输入、不涉及个人位置；
/// 一次探测消耗一次调用额度（配额按 Key 计）。
/// 成功判断：status==0。
/// Key 必填；SK 可选签名密钥（默认探测接口不支持 sig，故不附加；
/// 官方状态码 160 说明 sig 不适用于该请求类型）。
/// </summary>
public sealed class TencentLocationBalanceProvider : IApiBalanceProvider
{
    public const string ProviderId = "tencent-location";
    public const string DisplayName = "腾讯位置服务";
    public const string OfficialHost = "apis.map.qq.com";

    private const string ProbePath = "/ws/district/v1/list";

    private readonly ProviderHttpClient _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.TencentLocationDescription"),
        SupportsAccountBalance: false,
        SupportsKeyQuota: false,
        SupportedMetricKinds: new[] { BalanceMetricKind.Other },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                "api-key",
                "WebService API Key",
                L10n.Get("Provider.TencentLocationKeyHint"),
                IsDefault: true),
        },
        ApiKeyInputHint: L10n.Get("Provider.TencentLocationKeyInputHint"),
        HelpUrl: "https://lbs.qq.com/",
        SupportsTestConnection: true,
        DefaultBaseUrl: $"https://{OfficialHost}",
        ConfigFields: Array.Empty<ProviderConfigField>(),
        PrimaryMetricId: "tencent-location:service.availability",
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
                L10n.Get("Provider.TencentLocationKeyInputHint"),
                IsRequired: true),
            new ProviderCredentialSlot(
                CredentialSlots.Secret,
                "Provider.TencentLocationSecretLabel",
                "Provider.TencentLocationSecretHint",
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
        ProbeDescriptionKey: "Provider.TencentLocationProbeDescription");

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public TencentLocationBalanceProvider(IHttpRequestService http, AppLog? log = null)
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
        if (!credentials.TryGetValue(CredentialSlots.Primary, out var key)
            || string.IsNullOrWhiteSpace(key))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.MissingCredential,
                L10n.Get("Provider.ErrorNoKey"));
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.SendWithRetryAsync(
                () => BuildRequest(key.Trim()),
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
                L10n.Get("Provider.ErrorNetworkTencentLocation"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"腾讯位置服务探测发生意外错误: {ex.GetType().Name}");
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Get("Provider.ErrorUnexpected"));
        }
    }

    private static HttpRequestMessage BuildRequest(string key)
    {
        string query = string.Join(
            "&",
            $"key={Uri.EscapeDataString(key)}",
            "output=json");
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
            return FailureWithCode(BalanceErrorKind.CredentialInvalid, "Provider.Error401TencentLocation", response);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return FailureWithCode(BalanceErrorKind.PermissionDenied, "Provider.Error403TencentLocation", response);
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
    /// 腾讯位置服务官方状态码映射（lbs.qq.com/service/webService/webServiceGuide/status）。
    /// 300–408 为参数错误；500–531/600 为系统错误；未收录状态码一律 ProviderError。
    /// </summary>
    private static GeospatialStatus MapStatus(int status) =>
        status switch
        {
            0 => GeospatialStatus.Healthy,
            110 => GeospatialStatus.PermissionDenied,
            111 => GeospatialStatus.SignatureInvalid,
            112 => GeospatialStatus.IpWhitelistDenied,
            113 => GeospatialStatus.PermissionDenied,
            120 => GeospatialStatus.RateLimited,
            121 => GeospatialStatus.QuotaExceeded,
            160 or 161 => GeospatialStatus.InvalidResponse,
            190 => GeospatialStatus.CredentialInvalid,
            199 => GeospatialStatus.ServiceNotEnabled,
            301 => GeospatialStatus.ConfigurationMissing,
            311 => GeospatialStatus.KeyTypeMismatch,
            >= 300 and < 500 => GeospatialStatus.InvalidResponse,
            >= 500 and < 600 => GeospatialStatus.ProviderError,
            600 => GeospatialStatus.ProviderError,
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
