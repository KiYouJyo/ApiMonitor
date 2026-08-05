using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// SiliconFlow（硅基流动）余额查询实现。
/// 官方接口：GET https://api.siliconflow.cn/v1/user/info
/// Authorization: Bearer &lt;API_KEY&gt;。
/// 只读取余额监测需要的字段（totalBalance / chargeBalance / balance / grantedBalance），
/// 用户昵称、头像、邮箱等资料一律忽略且不保存；完整响应绝不写入日志或本地文件。
/// 官方响应语义：data.totalBalance 为总余额（= balance + chargeBalance），
/// 因此以 totalBalance 为主指标，绝不把多个字段再相加；字段缺失映射为 null。
/// 响应结构变化（必需字段缺失）时返回“响应结构暂不支持”，绝不显示为 0。
/// </summary>
public sealed class SiliconFlowBalanceProvider : IApiBalanceProvider
{
    public const string ProviderId = "siliconflow";
    public const string DisplayName = "SiliconFlow";
    public const string DefaultBaseUrl = "https://api.siliconflow.cn";

    private const string UserInfoEndpoint = DefaultBaseUrl + "/v1/user/info";

    public const string TotalMetricId = "siliconflow:balance.total.cny";
    public const string ChargeMetricId = "siliconflow:balance.charge.cny";
    public const string GrantedMetricId = "siliconflow:balance.granted.cny";
    public const string AvailableMetricId = "siliconflow:balance.available.cny";

    private readonly ProviderHttpClient _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.SiliconFlowDescription"),
        SupportsAccountBalance: true,
        SupportsKeyQuota: false,
        SupportedMetricKinds: new[] { BalanceMetricKind.MonetaryBalance },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                "api-key",
                "API Key",
                L10n.Get("Provider.SiliconFlowKeyHint"),
                IsDefault: true),
        },
        ApiKeyInputHint: L10n.Get("Provider.SiliconFlowKeyInputHint"),
        HelpUrl: "https://cloud.siliconflow.cn/",
        SupportsTestConnection: true,
        DefaultBaseUrl: DefaultBaseUrl,
        ConfigFields: Array.Empty<ProviderConfigField>(),
        PrimaryMetricId: TotalMetricId,
        Currency: "CNY",
        SupportsMultiCurrency: false,
        SupportsBreakdown: true,
        SupportsCredentialValidation: true,
        AllowCustomEndpoint: false);

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public SiliconFlowBalanceProvider(IHttpRequestService http, AppLog? log = null)
    {
        // 官方主机：国内站 api.siliconflow.cn（本应用默认端点）与
        // 国际站 api.siliconflow.com（官方文档亦公布）。两者都是 SiliconFlow 官方主机。
        _http = new ProviderHttpClient(
            http,
            new[] { "api.siliconflow.cn", "api.siliconflow.com" });
        _log = log;
    }

    public async Task<BalanceQueryResult> QueryBalanceAsync(
        ApiAccount account,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.MissingCredential,
                L10n.Get("Provider.ErrorNoKey"));
        }

        try
        {
            using var response = await _http.SendWithRetryAsync(
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    return request;
                },
                cancellationToken).ConfigureAwait(false);
            return await HandleResponseAsync(account, response, cancellationToken).ConfigureAwait(false);
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
                L10n.Get("Provider.ErrorNetworkSiliconFlow"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 只记录异常类型，绝不记录响应正文。
            _log?.Error($"SiliconFlow 查询发生意外错误: {ex.GetType().Name}");
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Get("Provider.ErrorUnexpected"));
        }
    }

    private static async Task<BalanceQueryResult> HandleResponseAsync(
        ApiAccount account,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unauthorized,
                L10n.Get("Provider.Error401"));
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Forbidden,
                L10n.Get("Provider.Error403"));
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.AccountNotFound,
                L10n.Get("Provider.Error404"));
        }

        if (response.StatusCode == (HttpStatusCode)429)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.RateLimited,
                L10n.Get("Provider.Error429"));
        }

        if ((int)response.StatusCode >= 500)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.ServerError,
                L10n.Format("Provider.ErrorServiceUnavailable", (int)response.StatusCode));
        }

        if (!response.IsSuccessStatusCode)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                L10n.Format("Provider.ErrorUnexpectedStatus", (int)response.StatusCode));
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
        if (root.ValueKind != JsonValueKind.Object)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidJson,
                L10n.Get("Provider.ErrorInvalidJson"));
        }

        // 官方成功码为 20000；其他 code 一律视为响应状态变化，不展示余额。
        if (root.TryGetProperty("code", out JsonElement codeElement)
            && codeElement.ValueKind == JsonValueKind.Number
            && codeElement.TryGetInt32(out int code)
            && code != 20000)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorUnsupportedFormat"));
        }

        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorUnsupportedFormat"));
        }

        // 主指标：data.totalBalance（总余额，官方响应中为 balance + chargeBalance 之和）。
        decimal? total = ReadAmount(data, "totalBalance");
        if (total is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorUnsupportedFormat"));
        }

        decimal? charge = ReadAmount(data, "chargeBalance");
        decimal? granted = ReadAmount(data, "grantedBalance");
        decimal? available = ReadAmount(data, "balance");

        bool isAvailable = string.IsNullOrWhiteSpace(ReadString(data, "status"))
            || string.Equals(ReadString(data, "status"), "normal", StringComparison.OrdinalIgnoreCase);

        var metrics = new List<BalanceMetric>
        {
            new()
            {
                MetricId = TotalMetricId,
                DisplayName = L10n.Get("Provider.SiliconFlowTotalMetricName"),
                Unit = "CNY",
                Kind = BalanceMetricKind.MonetaryBalance,
                AvailableAmount = total,
                TotalAmount = total,
                ToppedUpAmount = charge,
                GrantedAmount = granted,
                IsThresholdSupported = true,
            },
        };

        if (charge is not null)
        {
            metrics.Add(new BalanceMetric
            {
                MetricId = ChargeMetricId,
                DisplayName = L10n.Get("Provider.SiliconFlowChargeMetricName"),
                Unit = "CNY",
                Kind = BalanceMetricKind.MonetaryBalance,
                ToppedUpAmount = charge,
            });
        }

        if (granted is not null)
        {
            metrics.Add(new BalanceMetric
            {
                MetricId = GrantedMetricId,
                DisplayName = L10n.Get("Provider.SiliconFlowGrantedMetricName"),
                Unit = "CNY",
                Kind = BalanceMetricKind.MonetaryBalance,
                GrantedAmount = granted,
            });
        }

        if (available is not null)
        {
            metrics.Add(new BalanceMetric
            {
                MetricId = AvailableMetricId,
                DisplayName = L10n.Get("Provider.SiliconFlowBalanceMetricName"),
                Unit = "CNY",
                Kind = BalanceMetricKind.MonetaryBalance,
                AvailableAmount = available,
            });
        }

        return BalanceQueryResult.Success(new BalanceSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            ProviderId = account.ProviderId,
            IsAvailable = isAvailable,
            RetrievedAt = DateTimeOffset.UtcNow,
            Metrics = metrics,
        });
    }

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

    private static decimal? ReadAmount(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out decimal number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            string? text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text)
                && decimal.TryParse(
                    text,
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out decimal parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement parent, string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out JsonElement element)
            && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }
}
