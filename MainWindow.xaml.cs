using ApiMonitor.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace ApiMonitor;

public sealed partial class MainWindow : Window, IMainWindowController
{
    private readonly CompositionRoot _compositionRoot;
    private bool _allowClose;
    private bool _isVisible;

    public MainWindow(CompositionRoot compositionRoot)
    {
        InitializeComponent();
        Title = "ApiMonitor";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ApiMonitor.ico"));
        _compositionRoot = compositionRoot;
        AppWindow.Closing += OnAppWindowClosing;
    }

    /// <summary>页面根元素（x:Name 字段为私有，通过此属性公开给 App）。</summary>
    public Views.MainPage RootPage => RootPageControl;

    public bool IsVisible => _isVisible;

    public void Show()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter
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

        Activate();
        _isVisible = true;
    }

    public void Hide()
    {
        AppWindow.Hide();
        _isVisible = false;
    }

    public new void Close()
    {
        _allowClose = true;
        _isVisible = false;
        base.Close();
    }

    public void AllowClose() => _allowClose = true;

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || _compositionRoot.ExitCoordinator.IsExiting)
        {
            // 显式退出流程：放行真正关闭。
            return;
        }

        // 先取消关闭，防止异步对话框期间窗口被意外真正销毁。
        args.Cancel = true;

        if (_compositionRoot.WindowCloseController is { } controller)
        {
            await controller.HandleCloseRequestedAsync();
        }
    }
}
