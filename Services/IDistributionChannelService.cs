using Windows.ApplicationModel;

namespace ApiMonitor.Services;

/// <summary>
/// v1.0.0：统一分发渠道服务。关于页、更新检查、诊断信息与安装说明必须
/// 从该服务读取渠道信息；渠道本身来自构建配置（编译常量），运行时只用于
/// 与安装包实际身份做一致性校验（运行状况检查），绝不用于推断渠道。
/// </summary>
public interface IDistributionChannelService
{
    /// <summary>当前分发渠道（构建时确定）。</summary>
    DistributionChannel CurrentChannel { get; }

    /// <summary>用户可见产品版本（如 1.0.0）。</summary>
    string DisplayVersion { get; }

    /// <summary>当前包版本（如 1.0.0.1 / 1.0.0.0）。</summary>
    string PackageVersion { get; }

    /// <summary>渠道对应的更新来源说明（本地化文本由调用方渲染）。</summary>
    string UpdateSourceKey { get; }

    /// <summary>渠道对应的支持页面入口。</summary>
    string SupportPageUrl { get; }

    /// <summary>是否允许显示 GitHub 侧载安装说明（仅 GitHubSideload）。</summary>
    bool CanShowGitHubSideloadInstructions { get; }

    /// <summary>是否允许调用 StoreContext（仅 MicrosoftStore）。</summary>
    bool CanUseStoreContext { get; }

    /// <summary>渠道对应的期望 Package Identity Name。</summary>
    string ExpectedIdentityName { get; }

    /// <summary>渠道对应的期望 Publisher（CN=…）。</summary>
    string ExpectedPublisher { get; }

    /// <summary>渠道对应的期望 Package Family Name（Development 为空）。</summary>
    string ExpectedPackageFamilyName { get; }

    /// <summary>
    /// 安装包实际身份是否与当前渠道完全一致（Name + Publisher + PublisherId）。
    /// 未打包或身份不符时返回 false（Development 未打包视为不适用，不参与匹配）。
    /// </summary>
    bool InstalledIdentityMatchesChannel { get; }
}

/// <summary>
/// 真实实现：渠道读取构建常量；身份一致性通过 Package.Current 的实际
/// Name/Publisher/PublisherId 与渠道期望值精确匹配（不允许部分匹配）。
/// </summary>
public sealed class DistributionChannelService : IDistributionChannelService
{
    private readonly string _displayVersion;
    private readonly string _packageVersion;

    public DistributionChannelService(string displayVersion, string packageVersion)
    {
        _displayVersion = displayVersion;
        _packageVersion = packageVersion;
    }

    public DistributionChannel CurrentChannel => DistributionChannelConfig.Current;

    public string DisplayVersion => _displayVersion;

    public string PackageVersion => _packageVersion;

    public string UpdateSourceKey => CurrentChannel switch
    {
        DistributionChannel.MicrosoftStore => "Channel.UpdateSourceStore",
        DistributionChannel.GitHubSideload => "Channel.UpdateSourceGitHub",
        _ => "Channel.UpdateSourceDevelopment",
    };

    public string SupportPageUrl => "https://github.com/KiYouJyo/ApiMonitor/blob/main/SUPPORT.md";

    public bool CanShowGitHubSideloadInstructions => CurrentChannel == DistributionChannel.GitHubSideload;

    public bool CanUseStoreContext => CurrentChannel == DistributionChannel.MicrosoftStore;

    public string ExpectedIdentityName => DistributionChannelIdentity.ExpectedIdentityName(CurrentChannel);

    public string ExpectedPublisher => DistributionChannelIdentity.ExpectedPublisher(CurrentChannel);

    public string ExpectedPackageFamilyName => DistributionChannelIdentity.ExpectedPackageFamilyName(CurrentChannel);

    public bool InstalledIdentityMatchesChannel => ReadInstalledIdentityMatch();

    private static bool ReadInstalledIdentityMatch()
    {
        try
        {
            if (Package.Current is not { } package)
            {
                return false;
            }

            var id = package.Id;
            var identified = DistributionChannelIdentity.Identify(id.Name, id.Publisher, id.PublisherId);
            return identified == DistributionChannelConfig.Current;
        }
        catch
        {
            return false;
        }
    }
}
