using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>
/// v1.0.0：分发渠道测试。
/// 渠道必须来自构建配置（编译常量），身份识别必须 Name+Publisher+PublisherId
/// 三者完全匹配，禁止通过证书/Install.cmd/联网状态等运行时线索猜测。
/// </summary>
public sealed class DistributionChannelTests
{
    [Fact]
    public void Config_Current_IsOneOfTheThreeKnownChannels()
    {
        Assert.Contains(
            DistributionChannelConfig.Current,
            new[]
            {
                DistributionChannel.Development,
                DistributionChannel.GitHubSideload,
                DistributionChannel.MicrosoftStore,
            });
    }

    [Fact]
    public void Identify_ExactStoreTriple_ReturnsMicrosoftStore()
    {
        var channel = DistributionChannelIdentity.Identify(
            "JoKiy.ApiMonitor",
            "CN=C4E4B33A-7B77-4121-897C-7D720A5471F8",
            "c4e4b33a7b774121897c7d720a5471f8");

        Assert.Equal(DistributionChannel.MicrosoftStore, channel);
    }

    [Fact]
    public void Identify_StoreNameWithoutPublisher_ReturnsGitHubSideload()
    {
        // 部分匹配（只有 Name 相同）不得判定为 Store。
        var channel = DistributionChannelIdentity.Identify(
            "JoKiy.ApiMonitor",
            "CN=ApiMonitorDev",
            "00000000000000000000000000000000");

        Assert.Equal(DistributionChannel.GitHubSideload, channel);
    }

    [Fact]
    public void Identify_DevIdentity_ReturnsGitHubSideload()
    {
        var channel = DistributionChannelIdentity.Identify(
            "ApiMonitor",
            "CN=ApiMonitorDev",
            "cx0n152q1hsh2");

        Assert.Equal(DistributionChannel.GitHubSideload, channel);
    }

    [Fact]
    public void Identify_NullOrPartial_ReturnsGitHubSideload()
    {
        Assert.Equal(
            DistributionChannel.GitHubSideload,
            DistributionChannelIdentity.Identify(null, null, null));
        Assert.Equal(
            DistributionChannel.GitHubSideload,
            DistributionChannelIdentity.Identify("ApiMonitor", null, null));
    }

    [Fact]
    public void ExpectedIdentities_AreStablePerChannel()
    {
        Assert.Equal("ApiMonitor", DistributionChannelIdentity.ExpectedIdentityName(DistributionChannel.GitHubSideload));
        Assert.Equal("CN=ApiMonitorDev", DistributionChannelIdentity.ExpectedPublisher(DistributionChannel.GitHubSideload));
        Assert.Equal("JoKiy.ApiMonitor", DistributionChannelIdentity.ExpectedIdentityName(DistributionChannel.MicrosoftStore));
        Assert.Equal(
            "CN=C4E4B33A-7B77-4121-897C-7D720A5471F8",
            DistributionChannelIdentity.ExpectedPublisher(DistributionChannel.MicrosoftStore));
        Assert.Equal(
            "JoKiy.ApiMonitor_4wdwgytaw3v2m",
            DistributionChannelIdentity.ExpectedPackageFamilyName(DistributionChannel.MicrosoftStore));
    }

    [Fact]
    public void Service_ChannelComesFromBuildConfig_NotRuntimeState()
    {
        var service = new DistributionChannelService("1.0.0", "1.0.0.1");

        // 服务必须返回构建常量渠道（测试项目按 Development 编译），
        // 而不是根据本机已安装包/证书/网络推断。
        Assert.Equal(DistributionChannelConfig.Current, service.CurrentChannel);
        Assert.Equal("1.0.0", service.DisplayVersion);
        Assert.Equal("1.0.0.1", service.PackageVersion);
    }

    [Fact]
    public void Service_ChannelCapabilities_AreConsistentWithConfig()
    {
        var service = new DistributionChannelService("1.0.0", "1.0.0.1");

        Assert.Equal(
            service.CurrentChannel == DistributionChannel.MicrosoftStore,
            service.CanUseStoreContext);
        Assert.Equal(
            service.CurrentChannel == DistributionChannel.GitHubSideload,
            service.CanShowGitHubSideloadInstructions);
    }

    [Fact]
    public void Service_UpdateSourceKey_IsNeverGitHubForStore()
    {
        var service = new DistributionChannelService("1.0.0", "1.0.0.1");
        if (service.CurrentChannel == DistributionChannel.MicrosoftStore)
        {
            Assert.Equal("Channel.UpdateSourceStore", service.UpdateSourceKey);
        }
    }
}
