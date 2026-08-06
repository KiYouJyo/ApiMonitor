using System.Net;
using System.Text;
using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class UpdateCheckServiceTests
{
    private static string LatestJson(string tagName, string htmlUrl = "https://github.com/KiYouJyo/ApiMonitor/releases/tag/v1.0.0") =>
        $$"""
        {
          "tag_name": "{{tagName}}",
          "html_url": "{{htmlUrl}}",
          "name": "ApiMonitor {{tagName}}"
        }
        """;

    [Fact]
    public async Task NoUpdate_IsUpToDate()
    {
        var http = FakeHttpRequestService.Returning(LatestJson("v0.6.0"));
        var service = new GitHubUpdateService(http, "0.6.0");

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task NewVersion_IsUpdateAvailable()
    {
        var http = FakeHttpRequestService.Returning(LatestJson("v0.8.0"));
        var service = new GitHubUpdateService(http, "0.6.0");

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("0.8.0", result.LatestVersion);
        Assert.Contains("releases", result.ReleaseUrl);
    }

    [Fact]
    public async Task DevVersion_IsNewerThanLatest()
    {
        var http = FakeHttpRequestService.Returning(LatestJson("v0.5.0"));
        var service = new GitHubUpdateService(http, "0.6.0");

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.DevVersionNewer, result.Status);
    }

    [Fact]
    public async Task NotFound_IsFailure()
    {
        var http = FakeHttpRequestService.Returning("{}", HttpStatusCode.NotFound);
        var service = new GitHubUpdateService(http, "0.6.0");

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("404", result.ErrorMessage);
    }

    [Fact]
    public async Task Forbidden_IsRateLimited()
    {
        var http = FakeHttpRequestService.Returning("{}", HttpStatusCode.Forbidden);
        var service = new GitHubUpdateService(http, "0.6.0");

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("403", result.ErrorMessage);
    }

    [Fact]
    public async Task InvalidJson_IsFailure()
    {
        var http = FakeHttpRequestService.Returning("{not valid");
        var service = new GitHubUpdateService(http, "0.6.0");

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task NetworkError_IsFailure()
    {
        var http = FakeHttpRequestService.Throwing<HttpRequestException>();
        var service = new GitHubUpdateService(http, "0.6.0");

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task UsesApiMonitorUserAgent()
    {
        string? userAgent = null;
        var http = FakeHttpRequestService.Mutable(LatestJson("v0.6.0"));
        http.SetHandler((request, _) =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(LatestJson("v0.6.0"), Encoding.UTF8, "application/json"),
            });
        });
        var service = new GitHubUpdateService(http, "0.6.0");

        await service.CheckAsync(CancellationToken.None);

        Assert.Contains("ApiMonitor/0.6.0", userAgent);
    }

    [Fact]
    public void CompareVersions_HandlesFourSegments()
    {
        Assert.True(GitHubUpdateService.CompareVersions("0.6.0.1", "0.6.0") > 0);
        Assert.True(GitHubUpdateService.CompareVersions("0.6.0", "0.6.0.1") < 0);
        Assert.Equal(0, GitHubUpdateService.CompareVersions("0.6.0", "0.6.0"));
        Assert.True(GitHubUpdateService.CompareVersions("0.10.0", "0.9.9") > 0);
    }
}
