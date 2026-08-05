using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 自托管 GIS（SuperMap iServer / 通用 OGC）请求发送层（v0.9.0）。
/// 安全要求：
///   - 只允许 http/https，拒绝 file/ftp/data/自定义协议；
///   - HTTP 必须由用户在账户配置中显式确认（allowHttp）；
///   - 传输层禁用自动重定向（HttpRequestService allowAutoRedirect=false），
///     3xx 响应由 Provider 分类为 RedirectBlocked，凭据绝不跨 Origin 转发；
///   - localhost/私有地址允许（自托管正常场景），不扫描局域网、不探测其他端口。
/// </summary>
public sealed class SelfHostedHttpClient
{
    private readonly IHttpRequestService _http;

    public SelfHostedHttpClient(IHttpRequestService http)
    {
        _http = http;
    }

    /// <summary>校验并发送自托管请求；协议或确认缺失时抛出分类异常。</summary>
    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        bool allowHttp,
        CancellationToken cancellationToken)
    {
        Validate(request.RequestUri, allowHttp);
        return _http.SendAsync(request, cancellationToken);
    }

    /// <summary>校验自托管 URL 的协议与 HTTP 确认（不发送任何请求）。</summary>
    public static void Validate(Uri? uri, bool allowHttp)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            throw new SelfHostedRequestException(
                BalanceErrorKind.ProtocolViolation,
                L10n.Get("Provider.ErrorInvalidBaseUrl"));
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new SelfHostedRequestException(
                BalanceErrorKind.ProtocolViolation,
                L10n.Get("Provider.ErrorSchemeNotAllowed"));
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && !allowHttp)
        {
            throw new SelfHostedRequestException(
                BalanceErrorKind.ProtocolViolation,
                L10n.Get("Provider.ErrorHttpNotConfirmed"));
        }
    }
}

/// <summary>自托管请求安全校验失败（协议不允许 / HTTP 未确认）。</summary>
public sealed class SelfHostedRequestException : InvalidOperationException
{
    public BalanceErrorKind Kind { get; }

    public SelfHostedRequestException(BalanceErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }
}
