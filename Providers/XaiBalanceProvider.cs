using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// xAI 预付费 Credits 余额查询实现（Management API，不是模型推理 API）。
/// 官方接口：GET https://management-api.x.ai/v1/billing/teams/{team_id}/prepaid/balance
/// Authorization: Bearer &lt;MANAGEMENT_KEY&gt;；需要 Team ID（非敏感账户配置）。
///
/// 金额与符号语义（docs.x.ai 官方文档，2026-02-13 更新）：
///   - total 字段为 “Representation of USD Cents”，val 为字符串，单位是美分；
///   - changes.changeOrigin 语义：PURCHASE 的 amount 为负、SPEND 的 amount 为正，
///     说明该 API 采用账务方向记账：负值表示平台欠用户（即用户持有预付费 Credits）；
///   - 官方示例中 PURCHASE 1000 美分后 total=-1000，与“预付费 Credits 为负数记账”一致。
/// 因此用户可用的预付费余额（美元）= -(total.val) / 100；余额耗尽或透支时保留负值，
/// 禁止 Math.Abs、禁止翻转正负号、禁止把负值截断为 0、禁止把美分直接当美元显示。
/// 普通模型 API Key 不能用于余额查询，请求只发往 management-api.x.ai 官方主机。
/// </summary>
public sealed class XaiBalanceProvider : IApiBalanceProvider
{
    public const string ProviderId = "xai";
    public const string DisplayName = "xAI";
    public const string DefaultBaseUrl = "https://management-api.x.ai";

    public const string ManagementKeyMode = "management-key";
    public const string TeamIdField = "teamId";

    private const string PrepaidBalancePath = "/v1/billing/teams/{0}/prepaid/balance";

    public const string PrepaidMetricId = "xai:balance.prepaid.usd";

    private readonly ProviderHttpClient _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.XaiDescription"),
        SupportsAccountBalance: true,
        SupportsKeyQuota: false,
        SupportedMetricKinds: new[] { BalanceMetricKind.MonetaryBalance },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                ManagementKeyMode,
                "Management Key",
                L10n.Get("Provider.XaiKeyHint"),
                IsDefault: true),
        },
        ApiKeyInputHint: L10n.Get("Provider.XaiKeyInputHint"),
        HelpUrl: "https://console.x.ai/",
        SupportsTestConnection: true,
        DefaultBaseUrl: DefaultBaseUrl,
        ConfigFields: new[]
        {
            new ProviderConfigField(
                TeamIdField,
                "Provider.XaiTeamIdLabel",
                "Provider.XaiTeamIdHint",
                IsRequired: true,
                PlaceholderKey: "Provider.XaiTeamIdPlaceholder"),
        },
        PrimaryMetricId: PrepaidMetricId,
        Currency: "USD",
        SupportsMultiCurrency: false,
        SupportsBreakdown: false,
        SupportsCredentialValidation: true,
        AllowCustomEndpoint: false);

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public XaiBalanceProvider(IHttpRequestService http, AppLog? log = null)
    {
        _http = new ProviderHttpClient(http, new[] { "management-api.x.ai" });
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

        string? teamId = account.ProviderConfig.TryGetValue(TeamIdField, out var raw)
            ? raw?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(teamId))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.ConfigurationMissing,
                L10n.Get("Provider.ErrorMissingTeamId"));
        }

        string endpoint = DefaultBaseUrl
            + string.Format(
                CultureInfo.InvariantCulture,
                PrepaidBalancePath,
                Uri.EscapeDataString(teamId));

        try
        {
            using var response = await _http.SendWithRetryAsync(
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
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
                L10n.Get("Provider.ErrorNetworkXai"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"xAI 查询发生意外错误: {ex.GetType().Name}");
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
                L10n.Get("Provider.Error401Xai"));
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Forbidden,
                L10n.Get("Provider.Error403Xai"));
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.AccountNotFound,
                L10n.Get("Provider.Error404Team"));
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
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("total", out JsonElement total)
            || total.ValueKind != JsonValueKind.Object
            || !total.TryGetProperty("val", out JsonElement val))
        {
            // 余额结构缺失：返回“不支持的余额格式”，不向用户显示可能错误的金额。
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorUnsupportedBalanceFormat"));
        }

        decimal? totalCents = ReadCents(val);
        if (totalCents is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidResponse,
                L10n.Get("Provider.ErrorUnsupportedBalanceFormat"));
        }

        // 账务方向换算：预付费余额（美元）= -(美分) / 100。
        // 正值表示用户仍持有 Credits；负值表示欠费/透支，原样保留。
        decimal remainingUsd = -totalCents.Value / 100m;

        var metrics = new List<BalanceMetric>
        {
            new()
            {
                MetricId = PrepaidMetricId,
                DisplayName = L10n.Get("Provider.XaiPrepaidMetricName"),
                Unit = "USD",
                Kind = BalanceMetricKind.MonetaryBalance,
                AvailableAmount = remainingUsd,
                IsThresholdSupported = true,
            },
        };

        return BalanceQueryResult.Success(new BalanceSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            ProviderId = account.ProviderId,
            IsAvailable = remainingUsd > 0m,
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

    private static decimal? ReadCents(JsonElement element)
    {
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
