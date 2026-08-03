using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ApiMonitor.Models;
using ApiMonitor.Providers.Dto;
using ApiMonitor.Services;

namespace ApiMonitor.Providers;

/// <summary>
/// 真实的 DeepSeek 余额查询实现。
/// 接口约定（官方文档）：GET https://api.deepseek.com/user/balance
/// Accept: application/json；Authorization: Bearer &lt;API_KEY&gt;。
/// API Key 只放入请求头，绝不放入 URL、日志或异常信息。
/// </summary>
public sealed class DeepSeekBalanceProvider : IApiBalanceProvider
{
    public const string ProviderId = "deepseek";
    public const string DisplayName = "DeepSeek";

    private const string BalanceEndpoint = "https://api.deepseek.com/user/balance";

    private readonly IHttpRequestService _http;
    private readonly AppLog? _log;

    public ProviderInfo Info { get; } = new(
        ProviderId,
        DisplayName,
        L10n.Get("Provider.DeepSeekDescription"),
        SupportsAccountBalance: true,
        SupportsKeyQuota: false,
        SupportedMetricKinds: new[] { BalanceMetricKind.MonetaryBalance },
        CredentialOptions: new[]
        {
            new ProviderCredentialOption(
                "api-key",
                "API Key",
                L10n.Get("Provider.DeepSeekKeyHint"),
                IsDefault: true),
        },
        ApiKeyInputHint: "sk-…",
        HelpUrl: "https://platform.deepseek.com/",
        SupportsTestConnection: true);

    string IApiBalanceProvider.ProviderId => ProviderId;

    string IApiBalanceProvider.DisplayName => DisplayName;

    public DeepSeekBalanceProvider(IHttpRequestService http, AppLog? log = null)
    {
        _http = http;
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
            using var request = new HttpRequestMessage(HttpMethod.Get, BalanceEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return await HandleResponseAsync(account, response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户/应用取消：向上传播，不做错误分类。
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
                L10n.Get("Provider.ErrorNetworkDeepSeek"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"DeepSeek 查询发生意外错误: {ex.GetType().Name}");
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
                L10n.Get("Provider.Error403DeepSeek"));
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

        DeepSeekBalanceResponse? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DeepSeekBalanceResponse>(body);
        }
        catch (JsonException)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidJson,
                L10n.Get("Provider.ErrorInvalidJson"));
        }

        if (dto is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidJson,
                L10n.Get("Provider.ErrorInvalidJson"));
        }

        return BalanceQueryResult.Success(MapSnapshot(account, dto));
    }

    private static BalanceSnapshot MapSnapshot(ApiAccount account, DeepSeekBalanceResponse dto)
    {
        var metrics = new List<BalanceMetric>();

        foreach (var info in dto.BalanceInfos ?? new List<DeepSeekBalanceInfo>())
        {
            // 币种缺失或总余额无法解析的条目无法展示，跳过；
            // 未知币种代码原样保留（不做白名单校验），
            // 赠送/充值余额缺失时映射为 null（未知），而不是 0。
            if (string.IsNullOrWhiteSpace(info.Currency))
            {
                continue;
            }

            if (!TryParseBalance(info.TotalBalance, out var total))
            {
                continue;
            }

            string currency = info.Currency.Trim();
            decimal? granted = TryParseBalance(info.GrantedBalance, out var grantedValue) ? grantedValue : null;
            decimal? toppedUp = TryParseBalance(info.ToppedUpBalance, out var toppedUpValue) ? toppedUpValue : null;

            metrics.Add(new BalanceMetric
            {
                MetricId = $"deepseek:{currency}:total",
                DisplayName = $"{currency} 总余额",
                Unit = currency,
                Kind = BalanceMetricKind.MonetaryBalance,
                AvailableAmount = total,
                TotalAmount = total,
                GrantedAmount = granted,
                ToppedUpAmount = toppedUp,
                IsThresholdSupported = true,
            });
        }

        return new BalanceSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            ProviderId = account.ProviderId,
            IsAvailable = dto.IsAvailable,
            RetrievedAt = DateTimeOffset.UtcNow,
            Metrics = metrics,
        };
    }

    private static bool TryParseBalance(string? value, out decimal result) =>
        decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out result);
}
