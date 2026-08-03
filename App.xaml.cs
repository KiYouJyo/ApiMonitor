using ApiMonitor.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ApiMonitor;

public partial class App : Application
{
    private MainWindow? _window;
    private CompositionRoot? _compositionRoot;
    private readonly ISingleInstanceService _singleInstance;
    private readonly CancellationTokenSource _lifetime = new();
    private DispatcherQueue? _uiQueue;

    public App(ISingleInstanceService singleInstance)
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        _singleInstance = singleInstance;
    }

    /// <summary>
    /// 仅用于满足 XAML 生成的 XamlGeneratedMain 编译（自定义 Program.cs 才是真实入口，
    /// 该方法不会运行）。真实启动路径使用带 ISingleInstanceService 的构造函数。
    /// </summary>
    public App()
        : this(new SingleInstanceService())
    {
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _uiQueue = DispatcherQueue.GetForCurrentThread();
        _compositionRoot = new CompositionRoot(_uiQueue, _singleInstance);

        // 单实例：后续激活事件在初始化完成前订阅，避免错过重定向。
        // 先订阅 AppInstance.Activated（原生激活通道），再订阅业务事件转发。
        _singleInstance.SubscribeActivationEvents();
        _singleInstance.Activated += OnActivated;

        // 读取 StartupTask 系统状态缓存（不信任本地布尔值）。
        _ = _compositionRoot.StartupTaskService.RefreshStatusAsync(_lifetime.Token);

        // 初始化托盘图标（普通启动与登录启动都驻留通知区域）。
        _compositionRoot.TrayIconService.Initialize();

        // 登录启动（StartupTask）不弹出主窗口；普通启动显示主窗口。
        var activation = _singleInstance.GetInitialActivationKind();
        if (activation == AppActivationKind2.StartupTask)
        {
            // 仅驻留托盘：创建主窗口对象但保持隐藏（从托盘可随时打开）。
            CreateMainWindow(activate: false);
        }
        else
        {
            CreateMainWindow(activate: true);
        }

        _ = InitializeAndStartAsync();
    }

    private void CreateMainWindow(bool activate)
    {
        _window = new MainWindow(_compositionRoot!);
        _window.RootPage.ViewModel = _compositionRoot!.MainViewModel;
        _compositionRoot.AttachMainWindow(_window);

        if (activate)
        {
            _window.Show();
        }

        // Resolve the XamlRoot lazily at show time; it may still be null right after Activate.
        _compositionRoot.DialogService.Attach(() => _window.RootPage.XamlRoot);
    }

    private async Task InitializeAndStartAsync()
    {
        await _compositionRoot!.MainViewModel.InitializeAsync();

        if (_compositionRoot.MainViewModel.TraySettings is { } traySettings)
        {
            await traySettings.InitializeAsync();
        }

        _compositionRoot.MonitoringScheduler.Start(_lifetime.Token);
    }

    /// <summary>
    /// 主实例收到后续激活：普通启动重定向 → 显示并激活主窗口；
    /// 登录启动重定向 → 已在托盘驻留，忽略；退出中 → 安全忽略。
    /// AppInstance.Activated 事件可能在非 UI 线程触发，必须调度到 UI 线程操作窗口。
    /// </summary>
    private void OnActivated(AppActivationKind2 kind)
    {
        _uiQueue?.TryEnqueue(() => HandleActivation(kind));
    }

    private void HandleActivation(AppActivationKind2 kind)
    {
        // TryEnqueue 是异步调度，执行时需重新校验状态。
        if (_compositionRoot is null
            || _compositionRoot.ExitCoordinator.IsExiting
            || _window is null)
        {
            return;
        }

        if (kind == AppActivationKind2.Launch)
        {
            _window.Show();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 只记录异常类型与消息；消息中不含 API Key 或请求正文。
        _compositionRoot?.Log.Error($"未处理异常: {e.Exception.GetType().Name}: {e.Exception.Message}");
    }
}
