using ApiMonitor.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ApiMonitor;

public sealed partial class MainWindow : Window, IMainWindowController
{
    private readonly CompositionRoot _compositionRoot;
    private Views.MainPage? _liveRootPage;
    private bool _allowClose;
    private bool _isVisible;

    private bool _startupInitialized;
    private bool _startupLogoReady;
    private bool _startupFirstFrameRendered;
    private bool _startupMinimumElapsed;
    private bool _startupDismissStarted;

    public MainWindow(CompositionRoot compositionRoot)
    {
        InitializeComponent();
        _liveRootPage = RootPageControl;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = "ApiMonitor";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ApiMonitor.ico"));
        _compositionRoot = compositionRoot;
        AppWindow.Closing += OnAppWindowClosing;
    }

    /// <summary>页面根元素（x:Name 字段为私有，通过此属性公开给 App）。</summary>
    public Views.MainPage RootPage => _liveRootPage ?? RootPageControl;
    public FrameworkElement ThemeRoot => RootLayout;

    /// <summary>
    /// Like UrbanPlanToolbox: preserve the shared navigation ViewModel, but replace
    /// the entire localized visual shell and its page instances on language changes.
    /// The monitoring scheduler, accounts and single-instance process stay alive.
    /// </summary>
    public void RebuildLocalizedShell()
    {
        var previous = RootPage;
        var model = previous.ViewModel;
        if (model is null) return;

        var replacement = new Views.MainPage { ViewModel = model };
        previous.DetachShellEvents();
        ShellHost.Children.Clear();
        ShellHost.Children.Add(replacement);
        _liveRootPage = replacement;
        replacement.RefreshLocalizedContent();
    }

    public bool IsVisible => _isVisible;

    /// <summary>Synchronize the logo with the persisted application theme.</summary>
    public void ApplyStartupTheme(AppThemePreference preference)
    {
        RootLayout.RequestedTheme = preference switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateStartupLogoForTheme();
    }

    /// <summary>Release the splash only when the real initialization and logo gate finish.</summary>
    public void CompleteStartupPresentation()
    {
        _startupInitialized = true;
        TryCompleteStartupPresentation();
    }

    private void OnStartupThemeChanged(FrameworkElement sender, object args) =>
        UpdateStartupLogoForTheme();

    private void UpdateStartupLogoForTheme()
    {
        bool dark = RootLayout.ActualTheme == ElementTheme.Dark;
        StartupDarkLogo.Visibility = dark ? Visibility.Visible : Visibility.Collapsed;
        StartupLightLogo.Visibility = dark ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnStartupRootLoaded(object sender, RoutedEventArgs e)
    {
        UpdateStartupLogoForTheme();
        CompositionTarget.Rendering += OnStartupFirstFrameRendered;
        _ = EnsureStartupLogoGateAsync();
    }

    private void OnStartupLogoImageOpened(object sender, RoutedEventArgs e)
    {
        if ((RootLayout.ActualTheme == ElementTheme.Dark && ReferenceEquals(sender, StartupDarkLogo))
            || (RootLayout.ActualTheme != ElementTheme.Dark && ReferenceEquals(sender, StartupLightLogo)))
        {
            _startupLogoReady = true;
            TryCompleteStartupPresentation();
        }
    }

    private void OnStartupLogoImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // A missing image must not strand the user behind the splash.
        _startupLogoReady = true;
        TryCompleteStartupPresentation();
    }

    private async Task EnsureStartupLogoGateAsync()
    {
        await Task.Delay(1000);
        _startupLogoReady = true;
        TryCompleteStartupPresentation();
    }

    private void OnStartupFirstFrameRendered(object? sender, object args)
    {
        CompositionTarget.Rendering -= OnStartupFirstFrameRendered;
        if (_startupFirstFrameRendered) return;
        _startupFirstFrameRendered = true;
        _ = FinishMinimumStartupDurationAsync();
    }

    private async Task FinishMinimumStartupDurationAsync()
    {
        // Same visual timing contract as UrbanPlanToolbox: at least 500 ms
        // after the splash is first rendered, followed by a 200 ms fade.
        await Task.Delay(500);
        _startupMinimumElapsed = true;
        TryCompleteStartupPresentation();
    }

    private void TryCompleteStartupPresentation()
    {
        if (!_startupInitialized || !_startupLogoReady || !_startupMinimumElapsed
            || _startupDismissStarted)
            return;

        _startupDismissStarted = true;
        ShellHost.Opacity = 1;
        _ = FadeOutStartupOverlayAsync();
    }

    private async Task FadeOutStartupOverlayAsync()
    {
        try
        {
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var fade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(fade, StartupOverlay);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            storyboard.Children.Add(fade);
            storyboard.Completed += (_, _) => completion.TrySetResult(true);
            storyboard.Begin();
            await Task.WhenAny(completion.Task, Task.Delay(350));
        }
        catch
        {
            // Animation errors never prevent the app becoming usable.
        }
        finally
        {
            StartupOverlay.Visibility = Visibility.Collapsed;
            StartupOverlay.Opacity = 1;
            ShellHost.IsHitTestVisible = true;
        }
    }

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
