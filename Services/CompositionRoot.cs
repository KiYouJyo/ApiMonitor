using ApiMonitor.Providers;
using ApiMonitor.ViewModels;
using Microsoft.UI.Dispatching;

namespace ApiMonitor.Services;

/// <summary>
/// 清晰的组合根：集中创建服务、Provider 注册表与 ViewModel，
/// 不使用静态全局状态保存账户或密钥。
/// </summary>
public sealed class CompositionRoot
{
    public AppLog Log { get; }

    public MainViewModel MainViewModel { get; }

    public DialogService DialogService { get; }

    public MonitoringScheduler MonitoringScheduler { get; }

    public CompositionRoot(DispatcherQueue dispatcherQueue)
    {
        string dataDirectory = AppPaths.GetLocalDataDirectory();
        Directory.CreateDirectory(dataDirectory);

        Log = new AppLog(dataDirectory);
        var time = TimeProvider.System;

        var http = new HttpRequestService(TimeSpan.FromSeconds(15));
        var deepSeek = new DeepSeekBalanceProvider(http, Log);
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { deepSeek });

        var secretStore = new CredentialLockerSecretStore(Log);
        var accountStore = new JsonAccountStore(dataDirectory);
        var snapshotStore = new JsonBalanceSnapshotStore(dataDirectory);
        var clipboard = new WindowsClipboardService(dispatcherQueue, Log);

        var accountManager = new AccountManager(
            accountStore,
            snapshotStore,
            secretStore,
            registry,
            Log,
            time);

        DialogService = new DialogService(accountManager, Log);
        MonitoringScheduler = new MonitoringScheduler(accountManager, time, Log);
        MainViewModel = new MainViewModel(
            accountManager,
            DialogService,
            Log,
            clipboard,
            new UiThreadInvoker(dispatcherQueue));
    }

    public void Shutdown()
    {
        MonitoringScheduler.Stop();
        MainViewModel.Shutdown();
    }
}
