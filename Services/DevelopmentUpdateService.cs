namespace ApiMonitor.Services;

/// <summary>
/// 开发构建渠道：不进行任何网络更新检查。显示“当前为开发构建”，
/// 可查看版本信息/打开仓库，但绝不声称属于 Store 或 GitHub 正式版。
/// </summary>
public sealed class DevelopmentUpdateService : IUpdateService
{
    public DevelopmentUpdateService()
    {
    }

    public DistributionChannel Channel => DistributionChannel.Development;

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new UpdateCheckResult { Status = UpdateCheckStatus.DevelopmentBuild });
}
