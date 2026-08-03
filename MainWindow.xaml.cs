using ApiBalanceMonitor.Services;
using Microsoft.UI.Xaml;

namespace ApiBalanceMonitor;

public sealed partial class MainWindow : Window
{
    private readonly CompositionRoot _compositionRoot;

    public MainWindow(CompositionRoot compositionRoot)
    {
        _compositionRoot = compositionRoot;
        InitializeComponent();
        Title = "ApiBalanceMonitor";
        Closed += OnWindowClosed;
    }

    /// <summary>页面根元素（x:Name 字段为私有，通过此属性公开给 App）。</summary>
    public Views.MainPage RootPage => RootPageControl;

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // 应用退出时停止调度并取消所有在途异步操作。
        _compositionRoot.Shutdown();
    }
}
