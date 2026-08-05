using System.Net;

namespace ApiMonitor.Services;

/// <summary>
/// Provider 凭据请求的统一发送层：HTTPS 主机白名单 + 有限重试。
/// 每个 Provider 用其官方主机集合构造本实例；请求发出前校验 Scheme 与 Host，
/// 防止 API Key / Management Key 被发送到非官方主机。重试仅覆盖超时、
/// 429 与 5xx，401 / 403 / 404 / 配置类错误绝不自动重试，且全程可取消。
/// </summary>
public sealed class ProviderHttpClient
{
    public const int MaxAttempts = 3;

    private readonly IHttpRequestService _http;
    private readonly IReadOnlyList<string> _allowedHosts;
    private readonly Func<int, TimeSpan> _backoff;

    public ProviderHttpClient(
        IHttpRequestService http,
        IEnumerable<string> allowedHosts,
        Func<int, TimeSpan>? backoff = null)
    {
        _http = http;
        _allowedHosts = allowedHosts
            .Select(h => h.Trim().ToLowerInvariant())
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _backoff = backoff ?? (attempt => TimeSpan.FromMilliseconds(150 * (1 << (attempt - 1))));
    }

    /// <summary>
    /// 发送凭据请求：每次尝试都重新创建请求（工厂返回新消息），
    /// 发送前校验官方主机；对超时 / 429 / 5xx 做最多 <see cref="MaxAttempts"/> 次尝试。
    /// 最终响应由调用方负责释放。
    /// </summary>
    public async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var request = requestFactory();
            EnsureOfficialHost(request);

            HttpResponseMessage? response = null;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!ShouldRetry(response.StatusCode) || attempt >= MaxAttempts)
                {
                    return response;
                }

                response.Dispose();
                response = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                // HttpClient.Timeout 触发的超时：有限重试。
            }
            catch (TaskCanceledException)
            {
                throw;
            }

            await Task.Delay(_backoff(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

    private void EnsureOfficialHost(HttpRequestMessage request)
    {
        Uri? uri = request.RequestUri;
        if (uri is null
            || !uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("余额查询只允许发送到 HTTPS 官方主机。");
        }

        string host = uri.Host.ToLowerInvariant();
        if (!_allowedHosts.Contains(host, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("凭据请求目标主机不在官方白名单内。");
        }
    }
}
