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
/// v0.4.0 起负责托盘驻留、登录启动与统一退出协调的组装。
/// </summary>
public sealed class CompositionRoot
{
    /// <summary>托盘图标的稳定 GUID（不得每次启动变化）。</summary>
    internal static readonly Guid TrayIconId = new("8D3E7F1A-2B4C-4D5E-9F0A-1B2C3D4E5F60");

    internal static readonly string TrayIconPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcon.ico");

    public AppLog Log { get; }

    public MainViewModel MainViewModel { get; }

    public DialogService DialogService { get; }

    public MonitoringScheduler MonitoringScheduler { get; }

    public IWindowManager WindowManager { get; }

    public ICompactWindowService CompactWindowService { get; }

    public ICompactWindowSettingsStore CompactWindowSettingsStore { get; }

    public ITraySettingsStore TraySettingsStore { get; }

    public ITrayIconService TrayIconService { get; }

    public IStartupTaskService StartupTaskService { get; }

    public IApplicationExitCoordinator ExitCoordinator { get; }

    public ISingleInstanceService SingleInstanceService { get; }

    /// <summary>主窗口关闭行为控制器（AttachMainWindow 后可用）。</summary>
    public WindowCloseBehaviorController? WindowCloseController { get; private set; }

    private Action _showMainWindow = () => { };

    private Action _closeMainWindow = () => { };

    public CompositionRoot(DispatcherQueue dispatcherQueue, ISingleInstanceService singleInstanceService)
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

        // ------------------------------------------------------------------
        // v0.4.0：托盘驻留、登录启动与退出协调。
        // ------------------------------------------------------------------
        SingleInstanceService = singleInstanceService;
        TraySettingsStore = new JsonTraySettingsStore(dataDirectory);
        StartupTaskService = new StartupTaskService(Log);

        var trayHost = new TrayNativeHost(TrayIconPath, TrayIconId, Log);
        var statusProvider = new TrayStatusProvider(accountManager, Log);
        var menuService = new TrayMenuService();

        // 循环依赖（托盘命令 → 退出；退出 → 删除托盘图标）用闭包延迟绑定。
        ITrayIconService? trayRef = null;
        ExitCoordinator = new ApplicationExitCoordinator(
            MonitoringScheduler,
            () => trayRef!,
            CompactWindowService,
            TraySettingsStore,
            () => MainViewModel.Shutdown(),
            () => _closeMainWindow(),
            () => Application.Current.Exit(),
            Log);

        trayRef = new TrayIconService(
            trayHost,
            statusProvider,
            menuService,
            accountManager,
            CompactWindowService,
            StartupTaskService,
            TraySettingsStore,
            ExitCoordinator.BeginExit,
            Log,
            () => _showMainWindow());

        TrayIconService = trayRef;

        // v0.4.0：窗口关闭不再触发应用退出（托盘作为生命周期锚点）。
        MainViewModel.TraySettings = new TraySettingsViewModel(
            TraySettingsStore,
            StartupTaskService,
            ExitCoordinator,
            Log);
    }

    /// <summary>App 创建主窗口后调用：登记生命周期并绑定“打开/关闭主窗口”回调。</summary>
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
        _closeMainWindow = () =>
        {
            try
            {
                window.Close();
            }
            catch
            {
                // 关闭失败不阻塞退出流程。
            }
        };

        WindowCloseController = new WindowCloseBehaviorController(
            TraySettingsStore,
            DialogService,
            ExitCoordinator,
            window,
            Log);
    }
}
