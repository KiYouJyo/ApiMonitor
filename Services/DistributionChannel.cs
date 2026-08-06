namespace ApiMonitor.Services;

/// <summary>
/// v1.0.0：分发渠道。渠道必须在构建时确定（MSBuild DistributionChannel 属性 →
/// 编译常量），任何渠道相关行为（更新检查、关于页、诊断、安装说明）都必须
/// 从 IDistributionChannelService 读取，禁止运行时猜测。
/// </summary>
public enum DistributionChannel
{
    /// <summary>本地开发构建（未打包/调试）。</summary>
    Development,

    /// <summary>GitHub 侧载正式构建（CN=ApiMonitorDev 自签名 MSIX）。</summary>
    GitHubSideload,

    /// <summary>Microsoft Store 正式构建（Partner Center 官方身份）。</summary>
    MicrosoftStore,
}

/// <summary>
/// 构建时确定的当前渠道：由 DistributionChannel 属性注入的编译常量提供，
/// 不会在运行时访问 Package.Current、证书存储、Install.cmd、网络或配置目录。
/// </summary>
public static class DistributionChannelConfig
{
#if DISTRIBUTION_CHANNEL_MICROSOFT_STORE
    public const DistributionChannel Current = DistributionChannel.MicrosoftStore;
#elif DISTRIBUTION_CHANNEL_GITHUB_SIDELOAD
    public const DistributionChannel Current = DistributionChannel.GitHubSideload;
#else
    public const DistributionChannel Current = DistributionChannel.Development;
#endif
}

/// <summary>
/// 分发渠道身份常量与匹配规则。Store 身份来自 Partner Center 实际返回值
/// （apps get 9N6KR2XFMKQ2），GitHub 侧载身份保持既有开发身份不变。
/// 匹配必须同时满足 Name、Publisher 与 PublisherId 三者完全一致。
/// </summary>
public static class DistributionChannelIdentity
{
    // ---- GitHub 侧载（既有开发身份，保持不变） ----
    public const string SideloadName = "ApiMonitor";
    public const string SideloadPublisher = "CN=ApiMonitorDev";

    // ---- Microsoft Store 官方身份（Partner Center 2026-08-06 读取） ----
    public const string StoreName = "JoKiy.ApiMonitor";
    public const string StorePublisher = "CN=C4E4B33A-7B77-4121-897C-7D720A5471F8";
    public const string StorePublisherId = "c4e4b33a7b774121897c7d720a5471f8";
    public const string StorePackageFamilyName = "JoKiy.ApiMonitor_4wdwgytaw3v2m";
    public const string StoreProductId = "9N6KR2XFMKQ2";
    public const string StorePublisherDisplayName = "Jo Kiyō";

    /// <summary>
    /// 根据安装包实际身份识别渠道：只有三项完全匹配 Store 官方身份才判定为
    /// MicrosoftStore，其余一律 GitHubSideload（含未打包/未知身份）。
    /// </summary>
    public static DistributionChannel Identify(string? name, string? publisher, string? publisherId) =>
        string.Equals(name, StoreName, StringComparison.Ordinal) &&
        string.Equals(publisher, StorePublisher, StringComparison.Ordinal) &&
        string.Equals(publisherId, StorePublisherId, StringComparison.OrdinalIgnoreCase)
            ? DistributionChannel.MicrosoftStore
            : DistributionChannel.GitHubSideload;

    /// <summary>渠道对应的期望 Package Identity Name。</summary>
    public static string ExpectedIdentityName(DistributionChannel channel) => channel switch
    {
        DistributionChannel.MicrosoftStore => StoreName,
        _ => SideloadName,
    };

    /// <summary>渠道对应的期望 Publisher（CN=…）。</summary>
    public static string ExpectedPublisher(DistributionChannel channel) => channel switch
    {
        DistributionChannel.MicrosoftStore => StorePublisher,
        _ => SideloadPublisher,
    };

    /// <summary>渠道对应的期望 Package Family Name；Development 返回空（未打包）。</summary>
    public static string ExpectedPackageFamilyName(DistributionChannel channel) => channel switch
    {
        DistributionChannel.MicrosoftStore => StorePackageFamilyName,
        _ => string.Empty,
    };
}
