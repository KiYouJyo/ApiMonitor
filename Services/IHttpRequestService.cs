namespace ApiMonitor.Services;

/// <summary>可复用的 HTTP 请求服务抽象，便于测试替换。</summary>
public interface IHttpRequestService
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
