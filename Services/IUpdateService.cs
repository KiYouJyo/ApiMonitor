namespace ApiMonitor.Services;

/// <summary>更新检查结果状态。</summary>
public enum UpdateCheckStatus
{
    /// <summary>当前版本为最新。</summary>
    UpToDate,

    /// <summary>发现新版本。</summary>
    UpdateAvailable,

    /// <summary>当前版本高于最新发布（开发版本）。</summary>
    DevVersionNewer,

    /// <summary>当前为开发构建，不检查正式更新。</summary>
    DevelopmentBuild,

    /// <summary>当前渠道不支持该更新服务。</summary>
    UnsupportedChannel,

    /// <summary>检查失败（网络/超时/403/404/限速/非法 JSON/Store 服务不可用）。</summary>
    Failed,
}

/// <summary>更新检查结果。</summary>
public sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }

    /// <summary>最新发布版本号（UpdateAvailable 时有效）。</summary>
    public string? LatestVersion { get; init; }

    /// <summary>发布页/商店页 URL（UpdateAvailable 时有效）。</summary>
    public string? ReleaseUrl { get; init; }

    /// <summary>失败原因（Failed 时）。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Store 渠道是否允许用户主动请求下载并安装更新。</summary>
    public bool CanInstallFromStore { get; init; }
}

/// <summary>
/// v1.0.0：统一手动更新检查接口。只在用户点击“检查更新”后执行；
/// 实现按分发渠道选择，绝不跨渠道回退（Store 版不会打开 GitHub 下载页）。
/// </summary>
public interface IUpdateService
{
    /// <summary>该实现服务的分发渠道。</summary>
    DistributionChannel Channel { get; }

    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);
}
