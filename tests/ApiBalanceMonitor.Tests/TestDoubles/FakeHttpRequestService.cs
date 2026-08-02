using System.Net;
using System.Text;
using ApiBalanceMonitor.Services;

namespace ApiBalanceMonitor.Tests.TestDoubles;

public sealed class FakeHttpRequestService : IHttpRequestService
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public List<string> AuthorizationHeaders { get; } = new();

    public List<string> RequestUrls { get; } = new();

    private FakeHttpRequestService(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public static FakeHttpRequestService Returning(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));

    public static FakeHttpRequestService Throwing<TException>()
        where TException : Exception, new() =>
        new((_, _) => Task.FromException<HttpResponseMessage>(new TException()));

    public static FakeHttpRequestService Gated(TaskCompletionSource gate, string json) =>
        new(async (_, _) =>
        {
            await gate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
        if (request.Headers.Authorization is { } authorization)
        {
            AuthorizationHeaders.Add(authorization.ToString());
        }

        return _handler(request, cancellationToken);
    }
}
