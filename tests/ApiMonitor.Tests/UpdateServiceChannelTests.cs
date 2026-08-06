using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v1.0.0：三渠道更新服务行为测试。
/// GitHub 渠道访问 GitHub 服务；Store 渠道使用 StoreContext 且绝不返回
/// GitHub 安装链接；Development 渠道不声称正式更新。
/// </summary>
public sealed class UpdateServiceChannelTests
{
    [Fact]
    public async Task Development_ReturnsDevelopmentBuild_WithoutNetwork()
    {
        var service = new DevelopmentUpdateService();

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.DevelopmentBuild, result.Status);
        Assert.Null(result.ReleaseUrl);
        Assert.False(result.CanInstallFromStore);
        Assert.Equal(DistributionChannel.Development, service.Channel);
    }

    [Fact]
    public void GitHub_Service_IsBoundToGitHubSideloadChannel()
    {
        var http = ApiMonitor.Tests.TestDoubles.FakeHttpRequestService.Returning("{}");
        var service = new GitHubUpdateService(http, "1.0.0");

        Assert.Equal(DistributionChannel.GitHubSideload, service.Channel);
    }

    [Fact]
    public void Store_Service_IsBoundToMicrosoftStoreChannel()
    {
        var service = new MicrosoftStoreUpdateService(() => new nint(1));

        Assert.Equal(DistributionChannel.MicrosoftStore, service.Channel);
    }

    [Fact]
    public async Task Store_CheckWithoutWindow_ReturnsStoreWindowUnavailable_NotGitHub()
    {
        var service = new MicrosoftStoreUpdateService(() => null);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Null(result.ReleaseUrl);
        Assert.False(result.CanInstallFromStore);
        Assert.DoesNotContain("github.com", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Store_InstallWithoutPendingUpdate_ReturnsFailure()
    {
        var service = new MicrosoftStoreUpdateService(() => new nint(1));

        var result = await service.RequestInstallAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Null(result.ReleaseUrl);
    }

    [Fact]
    public void Store_ServiceSource_NeverReferencesGitHubReleaseUrl()
    {
        // 契约测试：Store 更新服务只允许 ms-windows-store 方案，
        // 源码中不得出现 GitHub 下载/发布页地址。
        string source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Services", "MicrosoftStoreUpdateService.cs"));
        Assert.Contains("ms-windows-store://", source);
        Assert.DoesNotContain("api.github.com", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("releases/tag", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Factory_UsesBuildTimeChannel_AndNeverCrossChannelFallsBack()
    {
        var service = UpdateServiceFactory.Create(
            ApiMonitor.Tests.TestDoubles.FakeHttpRequestService.Returning("{}"),
            "1.0.0",
            () => null);

        Assert.Equal(DistributionChannelConfig.Current, service.Channel);
        // Development 构建绝不使用 GitHub/Store 服务。
        if (DistributionChannelConfig.Current == DistributionChannel.Development)
        {
            Assert.IsType<DevelopmentUpdateService>(service);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ApiMonitor.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录。");
    }
}
