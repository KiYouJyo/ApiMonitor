namespace ApiMonitor.Services;

/// <summary>
/// 默认 HTTP 实现。超时由 HttpClient.Timeout 控制（当前为 15 秒），
/// 超时表现为 TaskCanceledException，由 Provider 分类为 Timeout。
/// </summary>
public sealed class HttpRequestService : IHttpRequestService
{
    private readonly HttpClient _client;

    /// <summary>
    /// v0.9.0：地理/GIS 探测默认关闭自动重定向（<paramref name="allowAutoRedirect"/>
    /// 为 false 时，3xx 响应原样返回，由 Provider 分类为 RedirectBlocked，
    /// 保证凭据绝不跟随跨主机/跨端口或 HTTPS→HTTP 重定向转发）。
    /// 旧 AI Provider 与更新检查保持默认 true，行为不变。
    /// </summary>
    public HttpRequestService(TimeSpan timeout, bool allowAutoRedirect = true)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip
                | System.Net.DecompressionMethods.Deflate,
        };
        _client = new HttpClient(handler) { Timeout = timeout };
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
}
