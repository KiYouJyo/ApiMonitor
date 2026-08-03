using ApiMonitor.Providers;
using ApiMonitor.ViewModels;
using ApiMonitor.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

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

    public IWindowManager WindowManager { get; }

    public ICompactWindowService CompactWindowService { get; }

    public ICompactWindowSettingsStore CompactWindowSettingsStore { get; }

    private Action _showMainWindow = () => { };

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
        var compactWindowSettingsStore = new CompactWindowSettingsStore(dataDirectory);
        var clipboard = new WindowsClipboardService(dispatcherQueue, Log);
        var uiThreadInvoker = new UiThreadInvoker(dispatcherQueue);
        var displayAreas = new DisplayAreaProvider();

        var accountManager = new AccountManager(
            accountStore,
            snapshotStore,
            secretStore,
            registry,
            Log,
            time);

        DialogService = new DialogService(accountManager, Log);
        MonitoringScheduler = new MonitoringScheduler(accountManager, time, Log);
        WindowManager = new WindowManager();
        CompactWindowSettingsStore = compactWindowSettingsStore;

        CompactWindowService = new CompactWindowService(() =>
        {
            var viewModel = new CompactWindowViewModel(
                accountManager,
                compactWindowSettingsStore,
                Log,
                uiThreadInvoker);
            var window = new CompactWindow(
                viewModel,
                compactWindowSettingsStore,
                displayAreas,
                Log);
            window.OpenMainWindowRequested += (_, _) => _showMainWindow();
            WindowManager.RegisterCompactWindow(window);
            return new WinUICompactWindowHost(window);
        });

        MainViewModel = new MainViewModel(
            accountManager,
            DialogService,
            Log,
            clipboard,
            uiThreadInvoker,
            () => CompactWindowService.OpenOrActivate());

        WindowManager.AllWindowsClosed += Shutdown;
    }

    /// <summary>App 创建主窗口后调用：登记生命周期并绑定“打开主窗口”回调。</summary>
    public void AttachMainWindow(MainWindow window)
    {
        WindowManager.RegisterMainWindow(window);
        _showMainWindow = () =>
        {
            try
            {
                if (window.AppWindow.Presenter is OverlappedPresenter
                    {
                        State: OverlappedPresenterState.Minimized
                    } presenter)
                {
                    presenter.Restore();
                }
            }
            catch
            {
                // 恢复失败时仍尝试激活。
            }

            window.Activate();
        };
    }

    public void Shutdown()
    {
        MonitoringScheduler.Stop();
        CompactWindowService.Shutdown();
        MainViewModel.Shutdown();
        Application.Current.Exit();
    }
}
