using System.Runtime.InteropServices;
using Windows.Services.Store;

namespace ApiMonitor.Services;

/// <summary>
/// Microsoft Store 渠道手动更新检查与安装请求：
///   - 只在用户点击“检查更新”后执行；
///   - 查询当前 Store 产品可用包更新（StoreContext）；
///   - 无更新显示已是最新；有更新显示 Store 更新状态；
///   - 下载/安装必须由用户主动请求（StoreContext 官方流程），并关联主窗口 HWND；
///   - 处理离线、超时、Store 服务不可用与系统策略限制；
///   - 绝不回退到 GitHub 下载页，不推荐/不下载自签名 GitHub 包。
/// </summary>
public sealed class MicrosoftStoreUpdateService : IUpdateService
{
    private readonly Func<nint?> _windowHandleProvider;
    private IReadOnlyList<StorePackageUpdate> _updates = Array.Empty<StorePackageUpdate>();

    public MicrosoftStoreUpdateService(Func<nint?> windowHandleProvider)
    {
        _windowHandleProvider = windowHandleProvider;
    }

    public DistributionChannel Channel => DistributionChannel.MicrosoftStore;

    /// <summary>查询当前 Store 产品的可用更新。</summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var context = CreateContext();
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken);
            _updates = updates ?? Array.Empty<StorePackageUpdate>();
            if (_updates.Count == 0)
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            }

            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
                ReleaseUrl = $"ms-windows-store://pdp/?ProductId={DistributionChannelIdentity.StoreProductId}",
                CanInstallFromStore = true,
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Get("Update.StoreCancelled"),
            };
        }
        catch (InvalidOperationException ex) when (ex.Message == "StoreWindowUnavailable")
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Get("Update.StoreWindowUnavailable"),
            };
        }
        catch (COMException ex)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Format("Update.StoreServiceUnavailable", $"0x{ex.HResult:X8}"),
            };
        }
        catch (Exception)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Get("Update.StoreServiceUnavailableGeneric"),
            };
        }
    }

    /// <summary>由用户主动请求的 Store 官方下载并安装流程（关联主窗口 HWND）。</summary>
    public async Task<UpdateCheckResult> RequestInstallAsync(CancellationToken cancellationToken)
    {
        if (_updates.Count == 0)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Get("Update.StoreNoPendingUpdate"),
            };
        }

        try
        {
            var context = CreateContext();
            var operation = context.RequestDownloadAndInstallStorePackageUpdatesAsync(_updates);
            var result = await operation.AsTask(cancellationToken);
            string state = result.OverallState.ToString();
            if (state.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            }

            if (state.StartsWith("Cancel", StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Failed,
                    ErrorMessage = L10n.Get("Update.StoreCancelled"),
                };
            }

            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Format("Update.StoreInstallState", state),
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Get("Update.StoreCancelled"),
            };
        }
        catch (InvalidOperationException ex) when (ex.Message == "StoreWindowUnavailable")
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Get("Update.StoreWindowUnavailable"),
            };
        }
        catch (COMException ex)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Format("Update.StoreServiceUnavailable", $"0x{ex.HResult:X8}"),
            };
        }
        catch (Exception)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = L10n.Get("Update.StoreInstallFailed"),
            };
        }
    }

    private StoreContext CreateContext()
    {
        nint? windowHandle = _windowHandleProvider();
        if (windowHandle is null || windowHandle == nint.Zero)
        {
            throw new InvalidOperationException("StoreWindowUnavailable");
        }

        var context = StoreContext.GetDefault();
        WinRT.Interop.InitializeWithWindow.Initialize(context, windowHandle.Value);
        return context;
    }
}
