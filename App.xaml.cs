using ApiMonitor.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ApiMonitor;

public partial class App : Application
{
    private MainWindow? _window;
    private CompositionRoot? _compositionRoot;
    private readonly CancellationTokenSource _lifetime = new();

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _compositionRoot = new CompositionRoot(DispatcherQueue.GetForCurrentThread());
        _window = new MainWindow(_compositionRoot);
        _window.RootPage.ViewModel = _compositionRoot.MainViewModel;
        _compositionRoot.AttachMainWindow(_window);
        _window.Activate();

        // Resolve the XamlRoot lazily at show time; it may still be null right after Activate.
        _compositionRoot.DialogService.Attach(() => _window.RootPage.XamlRoot);

        _ = InitializeAndStartAsync();
    }

    private async Task InitializeAndStartAsync()
    {
        await _compositionRoot!.MainViewModel.InitializeAsync();
        _compositionRoot.MonitoringScheduler.Start(_lifetime.Token);
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 只记录异常类型与消息；消息中不含 API Key 或请求正文。
        _compositionRoot?.Log.Error($"未处理异常: {e.Exception.GetType().Name}: {e.Exception.Message}");
    }
}
