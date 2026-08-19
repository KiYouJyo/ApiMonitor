using System.Runtime.InteropServices;
using Windows.Services.Store;

namespace ApiMonitor.Services;

public sealed class MicrosoftStoreUpdateService : IUpdateService
{
    private readonly Func<nint?> _windowHandleProvider;
    private IReadOnlyList<StorePackageUpdate> _updates = Array.Empty<StorePackageUpdate>();

    public MicrosoftStoreUpdateService(Func<nint?> windowHandleProvider) => _windowHandleProvider = windowHandleProvider;
    public DistributionChannel Channel => DistributionChannel.MicrosoftStore;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var context = CreateContext();
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken);
            _updates = updates ?? Array.Empty<StorePackageUpdate>();
            if (_updates.Count == 0) return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
                ReleaseUrl = $"ms-windows-store://pdp/?ProductId={DistributionChannelIdentity.StoreProductId}",
                CanInstallFromStore = true,
                CanInstallInApp = true,
            };
        }
        catch (OperationCanceledException) { return Failed(L10n.Get("Update.StoreCancelled")); }
        catch (InvalidOperationException ex) when (ex.Message == "StoreWindowUnavailable") { return Failed(L10n.Get("Update.StoreWindowUnavailable")); }
        catch (COMException ex) { return Failed(L10n.Format("Update.StoreServiceUnavailable", $"0x{ex.HResult:X8}")); }
        catch { return Failed(L10n.Get("Update.StoreServiceUnavailableGeneric")); }
    }

    public async Task<UpdateCheckResult> RequestInstallAsync(CancellationToken cancellationToken)
    {
        if (_updates.Count == 0) return Failed(L10n.Get("Update.StoreNoPendingUpdate"));
        try
        {
            var context = CreateContext();
            var result = await context.RequestDownloadAndInstallStorePackageUpdatesAsync(_updates).AsTask(cancellationToken);
            string state = result.OverallState.ToString();
            if (state.Equals("Completed", StringComparison.OrdinalIgnoreCase)) return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            if (state.StartsWith("Cancel", StringComparison.OrdinalIgnoreCase)) return Failed(L10n.Get("Update.StoreCancelled"));
            return Failed(L10n.Format("Update.StoreInstallState", state));
        }
        catch (OperationCanceledException) { return Failed(L10n.Get("Update.StoreCancelled")); }
        catch (InvalidOperationException ex) when (ex.Message == "StoreWindowUnavailable") { return Failed(L10n.Get("Update.StoreWindowUnavailable")); }
        catch (COMException ex) { return Failed(L10n.Format("Update.StoreServiceUnavailable", $"0x{ex.HResult:X8}")); }
        catch { return Failed(L10n.Get("Update.StoreInstallFailed")); }
    }

    private StoreContext CreateContext()
    {
        nint? windowHandle = _windowHandleProvider();
        if (windowHandle is null || windowHandle == nint.Zero) throw new InvalidOperationException("StoreWindowUnavailable");
        var context = StoreContext.GetDefault();
        WinRT.Interop.InitializeWithWindow.Initialize(context, windowHandle.Value);
        return context;
    }

    private static UpdateCheckResult Failed(string message) => new() { Status = UpdateCheckStatus.Failed, ErrorMessage = message };
}
