namespace ApiMonitor.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    DevVersionNewer,
    DevelopmentBuild,
    UnsupportedChannel,
    Failed,
}

public sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }
    public string? LatestVersion { get; init; }
    public string? ReleaseUrl { get; init; }
    public string? ErrorMessage { get; init; }
    public bool CanInstallFromStore { get; init; }
    /// <summary>当前渠道存在可由应用主动发起的安全安装路径。</summary>
    public bool CanInstallInApp { get; init; }
    /// <summary>用于 UI 展示的安装资产文件名，不包含本地路径。</summary>
    public string? InstallAssetName { get; init; }
}

public interface IUpdateService
{
    DistributionChannel Channel { get; }
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);
}
