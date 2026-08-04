using ApiMonitor.Providers;
using ApiMonitor.ViewModels;
using ApiMonitor.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Text.Json;

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

    /// <summary>数据洞察页 ViewModel（导航到数据洞察时按需加载，不重复启动调度器）。</summary>
    public InsightsViewModel InsightsViewModel { get; }

    /// <summary>关于页 ViewModel。</summary>
    public AboutViewModel AboutViewModel { get; }

    /// <summary>外观与语言设置（设置页“外观与语言”区）。</summary>
    public AppearanceSettingsViewModel AppearanceSettings { get; }

    /// <summary>数据管理（设置页“数据管理”区：便携备份导出/导入）。</summary>
    public DataManagementViewModel DataManagement { get; }

    public DialogService DialogService { get; }

    public MonitoringScheduler MonitoringScheduler { get; }

    public IWindowManager WindowManager { get; }

    public IFloatingWindowService FloatingWindowService { get; }

    public IFloatingWindowSettingsStore FloatingWindowSettingsStore { get; }

    public ITraySettingsStore TraySettingsStore { get; }

    public ITrayIconService TrayIconService { get; }

    public IStartupTaskService StartupTaskService { get; }

    public IApplicationExitCoordinator ExitCoordinator { get; }

    public ISingleInstanceService SingleInstanceService { get; }

    /// <summary>通知协调器（评估 + 展示 + 状态持久化）。</summary>
    public NotificationCoordinator NotificationCoordinator { get; }

    /// <summary>通知激活路由（打开账户 / 暂停 / 测试）。</summary>
    public NotificationActivationRouter NotificationActivationRouter { get; }

    /// <summary>主窗口关闭行为控制器（AttachMainWindow 后可用）。</summary>
    public WindowCloseBehaviorController? WindowCloseController { get; private set; }

    private Action _showMainWindow = () => { };

    private Action _closeMainWindow = () => { };

    private IntPtr _mainWindowHandle = IntPtr.Zero;

    /// <summary>
    /// 从 appearance-settings.json 读取持久化语言偏好并映射为语言代码
    /// （zh-CN/en-US/ja-JP）；失败或跟随系统时返回空（让 ResourceContext
    /// 回退系统语言）。不依赖 PrimaryLanguageOverride（未打包不可靠）。
    /// </summary>


    private static string ReadPersistedLanguage(string dataDirectory)
    {
        try
        {
            // This runs while the UI thread is constructing the composition root.
            // Do not synchronously wait on the async store here: its async file
            // operations can capture the UI context and prevent the first window
            // from ever being created.
            string path = Path.Combine(dataDirectory, JsonAppearanceSettingsStore.FileName);
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            var settings = JsonSerializer.Deserialize<AppearanceSettingsData>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (settings is null)
            {
                return string.Empty;
            }

            return settings.Language switch
            {
                nameof(AppLanguagePreference.ZhCn) => "zh-CN",
                nameof(AppLanguagePreference.EnUs) => "en-US",
                nameof(AppLanguagePreference.JaJp) => "ja-JP",
                _ => string.Empty,
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>主题服务（v0.6.0；主窗口/悬浮窗根元素注册处）。</summary>
    private readonly AppearanceService _appearanceService;

    /// <summary>v0.6.0 主题统一协调器：窗口根元素主题 + 原生标题栏颜色同步。</summary>
    private readonly WindowThemeCoordinator _themeCoordinator;

    public CompositionRoot(
        DispatcherQueue dispatcherQueue,
        ISingleInstanceService singleInstanceService,
        IAppNotificationService notificationService)
    {
        // v0.6.0：静态本地化入口必须最先初始化（VM 构造时即用 L10n.Get/Format，
        // 例如 MainViewModel.SubtitleText；若晚于 VM 创建会返回 [Missing: key]）。
        // 语言取自已持久化的 appearance-settings（不依赖 PrimaryLanguageOverride，
        // 该 API 在未打包与部分打包场景不可靠），通过全局 Language qualifier
        // 驱动 ResourceLoader 按目标语言解析三语资源。

        string dataDirectory = AppPaths.GetLocalDataDirectory();
        Directory.CreateDirectory(dataDirectory);

        string persistedLanguage = ReadPersistedLanguage(dataDirectory);
        if (!string.IsNullOrWhiteSpace(persistedLanguage))
        {
            try
            {
                Windows.ApplicationModel.Resources.Core.ResourceContext.SetGlobalQualifierValue(
                    "Language",
                    persistedLanguage);
            }
            catch
            {
                // ResourceLoader will fall back to the system language if the qualifier is unavailable.
            }
        }

        L10n.Initialize(key =>
        {
            try
            {
                // 用 WGA Core ResourceContext 指定语言，独立于 PrimaryLanguageOverride。
                var loader = Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse("Resources");
                string normalized = key.Replace('.', '/');
                string value = loader.GetString(normalized);
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch
            {
                return null;
            }
        });

        Log = new AppLog(dataDirectory);
        var time = TimeProvider.System;

        var http = new HttpRequestService(TimeSpan.FromSeconds(15));
        var deepSeek = new DeepSeekBalanceProvider(http, Log);
        var openRouter = new OpenRouterBalanceProvider(http, Log);
        var registry = new ProviderRegistry(new IApiBalanceProvider[] { deepSeek, openRouter });

        var secretStore = new CredentialLockerSecretStore(Log);
        var accountStore = new JsonAccountStore(dataDirectory);
        var snapshotStore = new JsonBalanceSnapshotStore(dataDirectory);
        var floatingWindowSettingsStore = new FloatingWindowSettingsStore(dataDirectory);
        var notificationSettingsStore = new JsonNotificationSettingsStore(dataDirectory);
        var notificationStateStore = new JsonNotificationStateStore(dataDirectory);
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
        FloatingWindowSettingsStore = floatingWindowSettingsStore;

        // v0.6.0：外观服务在悬浮窗创建前实例化，主题统一应用到所有窗口根元素。
        _appearanceService = new AppearanceService();
        _themeCoordinator = new WindowThemeCoordinator(_appearanceService);

        FloatingWindowService = new FloatingWindowService(() =>
        {
            var viewModel = new FloatingWindowViewModel(
                accountManager,
                floatingWindowSettingsStore,
                Log,
                uiThreadInvoker);
            var window = new FloatingBalanceWindow(
                viewModel,
                floatingWindowSettingsStore,
                displayAreas,
                Log);
            // 悬浮窗根元素注册到主题协调器：切换主题立即同步（含标题栏）。
            _themeCoordinator.RegisterWindow(window.AppWindow, window.RootGridElement, isMainWindow: false);
            window.Closed += (_, _) => _themeCoordinator.UnregisterWindow(window.RootGridElement);
            WindowManager.RegisterFloatingWindow(window);
            return new WinUIFloatingWindowHost(window);
        });

        // ------------------------------------------------------------------
        // v0.5.0：低余额通知（评估、展示、暂停、删除清理）。
        // 通知协调器在 MainViewModel 之前创建，供账户卡片读取暂停状态摘要。
        // ------------------------------------------------------------------
        var notificationCoordinator = new NotificationCoordinator(
            accountManager,
            notificationStateStore,
            notificationSettingsStore,
            new NotificationPolicyEvaluator(),
            notificationService,
            Log,
            time);
        NotificationCoordinator = notificationCoordinator;

        MainViewModel = new MainViewModel(
            accountManager,
            DialogService,
            Log,
            clipboard,
            uiThreadInvoker,
            accountId => FloatingWindowService.Show(accountId),
            (accountId, ct) => notificationCoordinator.GetSnoozedUntilAsync(accountId, ct));

        accountManager.RefreshCompleted += (_, e) =>
            _ = NotificationCoordinator.HandleRefreshCompletedAsync(e, CancellationToken.None);
        accountManager.AccountDeleted += (_, e) =>
            _ = NotificationCoordinator.RemoveAccountAsync(e.AccountId, CancellationToken.None);

        NotificationActivationRouter = new NotificationActivationRouter(
            accountManager,
            NotificationCoordinator,
            () => _showMainWindow(),
            () => MainViewModel.NavigateTo(AppPageKind.Home),
            accountId => MainViewModel.FocusAccount(accountId),
            (title, message) => MainViewModel.ShowPlainMessage(title, message));

        // ------------------------------------------------------------------
        // v0.4.0：托盘驻留、登录启动与退出协调。
        // ------------------------------------------------------------------
        SingleInstanceService = singleInstanceService;
        TraySettingsStore = new JsonTraySettingsStore(dataDirectory);
        StartupTaskService = new StartupTaskService(Log);

        var trayHost = new TrayNativeHost(TrayIconPath, TrayIconId, Log);
        var statusProvider = new TrayStatusProvider(accountManager, Log);
        // v0.6.0：统一字符串服务（托盘菜单、通知等代码文本按当前语言取）。
        var strings = new AppStrings();
        if (notificationService is AppNotificationService concreteNotification)
        {
            concreteNotification.SetStrings(strings);
        }

        var menuService = new TrayMenuService(strings);

        // 循环依赖（托盘命令 → 退出；退出 → 删除托盘图标）用闭包延迟绑定。
        ITrayIconService? trayRef = null;
        ExitCoordinator = new ApplicationExitCoordinator(
            MonitoringScheduler,
            () => trayRef!,
            FloatingWindowService,
            TraySettingsStore,
            () => MainViewModel.Shutdown(),
            () => _closeMainWindow(),
            () =>
            {
                // 应用退出时注销通知（正在退出时忽略新的通知动作）。
                notificationService.Unregister();
                Application.Current.Exit();
            },
            Log);

        trayRef = new TrayIconService(
            trayHost,
            statusProvider,
            menuService,
            accountManager,
            FloatingWindowService,
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

        MainViewModel.NotificationSettings = new NotificationSettingsViewModel(
            notificationSettingsStore,
            NotificationCoordinator.ShowTestNotification,
            OpenWindowsNotificationSettings,
            Log);

        // ------------------------------------------------------------------
        // v0.6.0：数据洞察、便携备份、外观与语言、关于页。
        // 全部共享同一账户服务与账户状态；不重复启动调度器、不重复订阅事件、
        // 不重复读取 Credential Locker。appearanceService 已在前方创建
        // （悬浮窗创建前），这里复用同一实例。
        // ------------------------------------------------------------------
        var appearanceStore = new JsonAppearanceSettingsStore(dataDirectory);
        var languageService = new LanguageService();

        AppearanceSettings = new AppearanceSettingsViewModel(
            appearanceStore,
            _appearanceService,
            languageService,
            requestRestart: () =>
            {
                try
                {
                    Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
                    return true;
                }
                catch
                {
                    return false;
                }
            },
            confirmRestartAsync: () => DialogService.ConfirmRestartAsync(CancellationToken.None),
            Log);

        // 主题偏好变化 → 应用到所有已注册窗口（根元素 + 标题栏）。
        _appearanceService.ThemeChanged += _themeCoordinator.ApplyTheme;

        var filePicker = new WinUIFilePickerService(() => _mainWindowHandle);

        var backupService = new PortableBackupService(
            dataDirectory,
            accountStore,
            snapshotStore,
            notificationSettingsStore,
            TraySettingsStore,
            floatingWindowSettingsStore,
            appearanceStore,
            registry.Infos.Select(p => p.ProviderId));

        DataManagement = new DataManagementViewModel(
            backupService,
            filePicker,
            new LocalDataFolderOpener(),
            Log);

        InsightsViewModel = new InsightsViewModel(
            accountManager,
            new InsightsHistoryProvider(snapshotStore),
            new TrendDataBuilder(),
            new ConsumptionEstimateService(),
            new CsvHistoryExporter(),
            filePicker,
            Log);

        var updateCheck = new UpdateCheckService(http, AppInfo.DisplayVersion);
        var diagnostics = new DiagnosticsInfoService(
            accountManager,
            notificationStateStore,
            StartupTaskService,
            languageService.CurrentLanguageCode,
            _appearanceService.Theme.ToString());

        AboutViewModel = new AboutViewModel(
            registry.Infos,
            updateCheck,
            diagnostics,
            clipboard,
            new DefaultExternalLinkLauncher(),
            new LocalDataFolderOpener(),
            filePicker,
            backupService,
            languageService.CurrentLanguageCode,
            _appearanceService.Theme.ToString(),
            Log);

        MainViewModel.AppearanceSettings = AppearanceSettings;
        MainViewModel.DataManagement = DataManagement;
        MainViewModel.Insights = InsightsViewModel;
        MainViewModel.About = AboutViewModel;
    }

    private static void OpenWindowsNotificationSettings()
    {
        try
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:notifications"));
        }
        catch
        {
            // 打开系统设置失败不影响应用。
        }
    }

    /// <summary>App 创建主窗口后调用：登记生命周期并绑定“打开/关闭主窗口”回调。</summary>
    public void AttachMainWindow(MainWindow window)
    {
        WindowManager.RegisterMainWindow(window);
        // 使用 IMainWindowController.Show（恢复最小化 + Activate + 可见标志），
        // 保证从托盘/单实例重定向打开时对已隐藏（AppWindow.Hide）的窗口同样有效。
        _showMainWindow = window.Show;
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

        // v0.6.0：文件选择器需要主窗口句柄（WinUI 3 picker 初始化）。
        try
        {
            _mainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
        catch
        {
            _mainWindowHandle = IntPtr.Zero;
        }

        // v0.6.0：主窗口根元素注册到主题协调器（切换主题立即生效，含标题栏）。
        try
        {
            if (window.RootPage is { } rootPage)
            {
                _themeCoordinator.RegisterWindow(window.AppWindow, rootPage, isMainWindow: true);
            }
        }
        catch
        {
            // 主题注册失败不影响应用。
        }
    }

}
