namespace ApiMonitor.Services;

/// <summary>
/// v1.0.0：按构建时渠道选择更新服务，绝不跨渠道回退：
///   Development    → DevelopmentUpdateService（不检查更新）；
///   GitHubSideload → GitHubUpdateService；
///   MicrosoftStore → MicrosoftStoreUpdateService（StoreContext + 主窗口 HWND）。
/// </summary>
public static class UpdateServiceFactory
{
    public static IUpdateService Create(
        IHttpRequestService http,
        string displayVersion,
        Func<nint?> mainWindowHandleProvider)
    {
        return DistributionChannelConfig.Current switch
        {
            DistributionChannel.MicrosoftStore => new MicrosoftStoreUpdateService(mainWindowHandleProvider),
            DistributionChannel.GitHubSideload => new GitHubUpdateService(http, displayVersion),
            _ => new DevelopmentUpdateService(),
        };
    }
}
