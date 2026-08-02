using ApiBalanceMonitor.Services;
using Microsoft.UI.Xaml;

namespace ApiBalanceMonitor;

public partial class App : Application
{
    private MainWindow? _window;
    private CompositionRoot? _compositionRoot;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _compositionRoot = new CompositionRoot();
        _window = new MainWindow(_compositionRoot);
        _window.RootPage.ViewModel = _compositionRoot.MainViewModel;
        _compositionRoot.DialogService.Attach(_window.RootPage.XamlRoot);
        _window.Activate();

        _ = _compositionRoot.MainViewModel.InitializeAsync();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 只记录异常类型与消息；消息中不含 API Key 或请求正文。
        _compositionRoot?.Log.Error($"未处理异常: {e.Exception.GetType().Name}: {e.Exception.Message}");
    }
}
