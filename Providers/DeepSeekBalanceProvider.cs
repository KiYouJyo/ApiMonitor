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
                "未提供 API Key。");
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
                "请求超时（15 秒），请稍后重试。");
        }
        catch (HttpRequestException)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Network,
                "无法连接 DeepSeek 服务，请检查网络或 DNS。");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Error($"DeepSeek 查询发生意外错误: {ex.GetType().Name}");
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                "查询时发生意外错误，请稍后重试。");
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
                "API Key 无效或已过期（401）。");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Forbidden,
                "访问被拒绝（403），请检查账户权限。");
        }

        if (response.StatusCode == (HttpStatusCode)429)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.RateLimited,
                "请求过于频繁（429），请稍后重试。");
        }

        if ((int)response.StatusCode >= 500)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.ServerError,
                $"DeepSeek 服务暂时不可用（HTTP {(int)response.StatusCode}）。");
        }

        if (!response.IsSuccessStatusCode)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.Unknown,
                $"服务返回了意外的 HTTP 状态码 {(int)response.StatusCode}。");
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.EmptyContent,
                "接口返回了空内容。");
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
                "接口返回的 JSON 格式无法解析。");
        }

        if (dto is null)
        {
            return BalanceQueryResult.Failure(
                BalanceErrorKind.InvalidJson,
                "接口返回的 JSON 格式无法解析。");
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
