using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// Moonshot AI / Kimi 余额查询实现。
/// 官方接口（国内站）：GET https://api.moonshot.cn/v1/users/me/balance
/// Authorization: Bearer &lt;API_KEY&gt;；只调用余额接口，绝不发送模型推理请求。
/// 官方响应（platform.moonshot.cn/docs/api/balance）：
///   code=0 表示成功；data.available_balance（人民币元，含现金+代金券）、
///   data.voucher_balance（代金券，不可为负）、data.cash_balance（现金，可为负）。
///   当 available_balance 小于等于 0 时用户无法调用推理 API。
/// 数值缺失一律映射为 null（未知），绝不显示为 0；不重复把现金与代金券相加。
/// </summary>
public sealed class MoonshotBalanceProvider : IApiBalanceProvider
{
    public const string ProviderId = "moonshot";
    public const string DisplayName = "Moonshot";
    public const string DefaultBaseUrl = "https://api.moonshot.cn";

    private const string BalanceEndpoint = DefaultBaseUrl + "/v1/users/me/balance";

    public const string AvailableMetricId = "moonshot:balance.available.cny";
    public const string CashMetricId = "moonshot:balance.cash.cny";
    public const string VoucherMetricId = "moonshot:balance.voucher.cny";

    private readonly ProviderHttpClient _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.MoonshotDescription"),
        SupportsAccountBalance: true,
        SupportsKeyQuota: false,
        SupportedMetricKinds: new[] { BalanceMetricKind.MonetaryBalance },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                "api-key",
                "API Key",
                L10n.Get("Provider.MoonshotKeyHint"),
                IsDefault: true),
        },
        ApiKeyInputHint: L10n.Get("Provider.MoonshotKeyInputHint"),
        HelpUrl: "https://platform.moonshot.cn/",
        SupportsTestConnection: true,
        DefaultBaseUrl: DefaultBaseUrl,
        ConfigFields: Array.Empty<ProviderConfigField>(),
        PrimaryMetricId: AvailableMetricId,
        Currency: "CNY",
        SupportsMultiCurrency: false,
        SupportsBreakdown: true,
        SupportsCredentialValidation: true,
        AllowCustomEndpoint: false);

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public MoonshotBalanceProvider(IHttpRequestService http, AppLog? log = null)
    {
        _http = new ProviderHttpClient(http, new[] { "api.moonshot.cn" });
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
                    var request = new HttpRequestMessage(HttpMethod.Get, BalanceEndpoint);
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
                L10n.Get("Provider.ErrorNetworkMoonshot"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"Moonshot 查询发生意外错误: {ex.GetType().Name}");
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

        // code=0 表示成功；出现其他 code 视为响应结构/状态变化，不展示余额。
        if (root.TryGetProperty("code", out JsonElement codeElement)
            && codeElement.ValueKind == JsonValueKind.Number
            && codeElement.TryGetInt32(out int code)
            && code != 0)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorUnsupportedFormat"));
        }

        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorMissingBalanceFields"));
        }

        decimal? available = ReadAmount(data, "available_balance");
        if (available is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorMissingBalanceFields"));
        }

        decimal? cash = ReadAmount(data, "cash_balance");
        decimal? voucher = ReadAmount(data, "voucher_balance");

        // 官方：available_balance 小于等于 0 时用户无法调用推理 API。
        bool isAvailable = available.Value > 0m;
        if (root.TryGetProperty("status", out JsonElement statusElement)
            && statusElement.ValueKind == JsonValueKind.False)
        {
            isAvailable = false;
        }

        var metrics = new List<BalanceMetric>
        {
            new()
            {
                MetricId = AvailableMetricId,
                DisplayName = L10n.Get("Provider.MoonshotAvailableMetricName"),
                Unit = "CNY",
                Kind = BalanceMetricKind.MonetaryBalance,
                AvailableAmount = available,
                TotalAmount = available,
                // 官方定义：可用余额 = 现金 + 代金券，仅在主指标上附分项，
                // 不再把现金与代金券重复相加。
                GrantedAmount = voucher,
                ToppedUpAmount = cash,
                IsThresholdSupported = true,
            },
        };

        if (cash is not null)
        {
            metrics.Add(new BalanceMetric
            {
                MetricId = CashMetricId,
                DisplayName = L10n.Get("Provider.MoonshotCashMetricName"),
                Unit = "CNY",
                Kind = BalanceMetricKind.MonetaryBalance,
                AvailableAmount = cash,
                ToppedUpAmount = cash,
            });
        }

        if (voucher is not null)
        {
            metrics.Add(new BalanceMetric
            {
                MetricId = VoucherMetricId,
                DisplayName = L10n.Get("Provider.MoonshotVoucherMetricName"),
                Unit = "CNY",
                Kind = BalanceMetricKind.MonetaryBalance,
                AvailableAmount = voucher,
                GrantedAmount = voucher,
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
}
