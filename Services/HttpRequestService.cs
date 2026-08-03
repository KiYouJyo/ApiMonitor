namespace ApiMonitor.Services;

/// <summary>
/// 默认 HTTP 实现。超时由 HttpClient.Timeout 控制（当前为 15 秒），
/// 超时表现为 TaskCanceledException，由 Provider 分类为 Timeout。
/// </summary>
public sealed class HttpRequestService : IHttpRequestService
{
    private readonly HttpClient _client;

    public HttpRequestService(TimeSpan timeout)
    {
        _client = new HttpClient { Timeout = timeout };
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
}
