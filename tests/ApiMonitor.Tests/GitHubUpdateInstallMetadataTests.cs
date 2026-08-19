using ApiMonitor.Services;
using ApiMonitor.Tests.TestDoubles;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class GitHubUpdateInstallMetadataTests
{
    [Fact]
    public async Task ReleaseWithMsixBundle_EnablesInAppInstall()
    {
        const string json = """
        {
          "tag_name": "v1.1.0",
          "html_url": "https://github.com/KiYouJyo/ApiMonitor/releases/tag/v1.1.0",
          "assets": [
            {
              "name": "ApiMonitor-1.1.0-x64.msixbundle",
              "browser_download_url": "https://github.com/KiYouJyo/ApiMonitor/releases/download/v1.1.0/ApiMonitor-1.1.0-x64.msixbundle"
            }
          ]
        }
        """;
        var service = new GitHubUpdateService(FakeHttpRequestService.Returning(json), "1.0.0");
        var result = await service.CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.CanInstallInApp);
        Assert.EndsWith(".msixbundle", result.InstallAssetName, StringComparison.OrdinalIgnoreCase);
        Assert.True(service.HasInstallablePackage);
    }

    [Fact]
    public async Task ReleaseWithoutPackage_RemainsCheckableButNotInstallable()
    {
        const string json = """{ "tag_name":"v1.1.0", "html_url":"https://github.com/KiYouJyo/ApiMonitor/releases/tag/v1.1.0", "assets":[] }""";
        var service = new GitHubUpdateService(FakeHttpRequestService.Returning(json), "1.0.0");
        var result = await service.CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.False(result.CanInstallInApp);
    }
}
