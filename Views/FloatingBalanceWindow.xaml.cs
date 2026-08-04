using ApiMonitor.Services;
using ApiMonitor.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace ApiMonitor.Views;

/// <summary>
/// 悬浮余额窗（v0.7.0）：只做视图职责（置顶、无任务栏、位置/尺寸、事件转发），
/// 业务状态全部来自 FloatingWindowViewModel 与账户服务。
/// </summary>
public sealed partial class FloatingBalanceWindow : Window
{
    private readonly FloatingWindowViewModel _viewModel;
    private readonly IFloatingWindowSettingsStore _settingsStore;
    private readonly IDisplayAreaProvider _displayAreas;
    private readonly AppLog _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly InputNonClientPointerSource _nonClientPointerSource;
    private CancellationTokenSource? _positionSaveDebounce;
    private PointInt32? _lastSavedPosition;
    private bool _boundsRestored;
    private bool _closing;
    private bool _diagnosticLogged;
    private int _positionSaveCount;

    public FloatingBalanceWindow(
        FloatingWindowViewModel viewModel,
        IFloatingWindowSettingsStore settingsStore,
        IDisplayAreaProvider displayAreas,
        AppLog log)
    {
        _viewModel = viewModel;
        _settingsStore = settingsStore;
        _displayAreas = displayAreas;
        _log = log;

        InitializeComponent();
        Title = string.Empty;
        SingleRootSurface.DataContext = viewModel;
        _nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);

        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
        AppWindow.Changed += OnAppWindowChanged;
        SingleRootSurface.Loaded += OnSurfaceLoaded;

        ApplyPresenter();
    }

    /// <summary>窗口唯一根 Surface（供主题服务注册；内部仅供 CompositionRoot 使用）。</summary>
    internal FrameworkElement RootGridElement => SingleRootSurface;

    /// <summary>切换悬浮窗账户（宿主调用；初始化完成后应用）。</summary>
    internal void SelectAccount(string accountId)
    {
        _ = SelectAccountCoreAsync(accountId);
    }

    private async Task SelectAccountCoreAsync(string accountId)
    {
        try
        {
            await _viewModel.InitializeAsync(_lifetime.Token);
            await _viewModel.ShowAccountAsync(accountId, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"切换悬浮窗账户失败: {ex.GetType().Name}");
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (!_boundsRestored)
        {
            _boundsRestored = true;
            _ = RestoreBoundsAsync();
            _ = _viewModel.InitializeAsync(_lifetime.Token);
        }

        ApplyToolWindowStyle();
        _ = ApplyPhysicalSizeAfterActivationAsync();
    }

    private async Task ApplyPhysicalSizeAfterActivationAsync()
    {
        try
        {
            await Task.Delay(100, _lifetime.Token);
            int physicalSize = GetPhysicalFixedSize();
            AppWindow.Resize(new SizeInt32(physicalSize, physicalSize));

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _ = NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                AppWindow.Position.X,
                AppWindow.Position.Y,
                physicalSize,
                physicalSize,
                NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_FRAMECHANGED);
            RegisterCaptionRegion();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"鎮诞绐楀浐瀹氬昂瀵稿け璐? {ex.GetType().Name}");
        }
    }

    /// <summary>无标题栏、无边框、固定尺寸并始终置顶。</summary>
    private void ApplyPresenter()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
        }
        catch (Exception ex)
        {
            // 置顶失败不崩溃；窗口仍可用。
            _log.Error($"设置悬浮窗置顶失败: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// 将宿主切换为真正的无边框工具窗口：不进入任务栏/Alt+Tab，
    /// 去掉系统标题栏、边框和调整大小样式。拖动由内容区域转发为标题拖动消息。
    /// </summary>
    private void ApplyToolWindowStyle()
    {
        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int windowStyle = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_STYLE);
            windowStyle &= ~unchecked((int)(
                NativeMethods.WS_CAPTION
                | NativeMethods.WS_THICKFRAME
                | NativeMethods.WS_MINIMIZEBOX
                | NativeMethods.WS_MAXIMIZEBOX
                | NativeMethods.WS_SYSMENU));
            windowStyle |= unchecked((int)NativeMethods.WS_POPUP);
            _ = NativeMethods.SetWindowLongW(hwnd, NativeMethods.GWL_STYLE, windowStyle);

            int style = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE);
            style &= ~unchecked((int)NativeMethods.WS_EX_APPWINDOW);
            _ = NativeMethods.SetWindowLongW(
                hwnd,
                NativeMethods.GWL_EXSTYLE,
                style | (int)NativeMethods.WS_EX_TOOLWINDOW);
            AppWindow.Resize(new SizeInt32(GetPhysicalFixedSize(), GetPhysicalFixedSize()));
            _ = NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                AppWindow.Position.X,
                AppWindow.Position.Y,
                GetPhysicalFixedSize(),
                GetPhysicalFixedSize(),
                NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_FRAMECHANGED);
            RegisterCaptionRegion();
        }
        catch (Exception ex)
        {
            _log.Error($"设置悬浮窗工具窗口样式失败: {ex.GetType().Name}");
        }
    }

    private void RegisterCaptionRegion()
    {
        _nonClientPointerSource.SetRegionRects(
            NonClientRegionKind.Caption,
            new[]
            {
                new RectInt32(
                    0,
                    0,
                    GetPhysicalFixedSize(),
                    GetPhysicalFixedSize()),
            });
    }

    private int GetPhysicalFixedSize()
    {
        double scale = SingleRootSurface.XamlRoot?.RasterizationScale ?? 1;
        return (int)Math.Round(FloatingWindowDefaults.FixedSize * scale);
    }

    private void OnSurfaceLoaded(object sender, RoutedEventArgs args)
    {
        if (_diagnosticLogged)
        {
            return;
        }

        _diagnosticLogged = true;
        _log.Info(
            $"悬浮窗诊断: AppWindow={AppWindow.Size.Width}x{AppWindow.Size.Height}, " +
            $"ClientSize={AppWindow.ClientSize.Width}x{AppWindow.ClientSize.Height}, " +
            $"Surface={SingleRootSurface.ActualWidth:0.##}x{SingleRootSurface.ActualHeight:0.##}, " +
            $"CornerRadius=18, Caption={GetPhysicalFixedSize()}x{GetPhysicalFixedSize()} physical, " +
            $"Scale={SingleRootSurface.XamlRoot?.RasterizationScale ?? 1:0.##}, SaveCount={_positionSaveCount}.");
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_closing || !args.DidPositionChange)
        {
            return;
        }

        _positionSaveDebounce?.Cancel();
        _positionSaveDebounce?.Dispose();
        _positionSaveDebounce = new CancellationTokenSource();
        _ = SaveBoundsAfterDelayAsync(_positionSaveDebounce.Token);
    }

    private async Task SaveBoundsAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken);
            await SaveBoundsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RestoreBoundsAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(_lifetime.Token);
            try
            {
                var areas = _displayAreas.GetAll();
                var restored = WindowPositionRestorer.Restore(
                    settings.X,
                    settings.Y,
                    settings.Width,
                    settings.Height,
                    settings.LastDisplayId,
                    areas);

                ApplyWindowBounds(restored);
            }
            catch (InvalidCastException)
            {
                // Some Windows App SDK builds can fail while projecting DisplayArea.
                // Preserve the user's saved native coordinates even when monitor metadata is unavailable.
                ApplyWindowBounds(new PixelRect(
                    (int)Math.Round(settings.X ?? 0),
                    (int)Math.Round(settings.Y ?? 0),
                    (int)FloatingWindowDefaults.FixedSize,
                    (int)FloatingWindowDefaults.FixedSize));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // 位置恢复失败不影响启动。
            _log.Error($"恢复悬浮窗位置失败: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// 直接调整无边框宿主的实际窗口矩形。无标题栏后客户区与窗口外框一致，
    /// 因此这里保证宿主、根布局和圆角方块使用同一个固定尺寸。
    /// </summary>
    private void ApplyWindowBounds(PixelRect bounds)
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            bounds.X,
            bounds.Y,
            GetPhysicalFixedSize(),
            GetPhysicalFixedSize(),
            NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_FRAMECHANGED);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        AppWindow.Changed -= OnAppWindowChanged;
        SingleRootSurface.Loaded -= OnSurfaceLoaded;
        _nonClientPointerSource.ClearRegionRects(NonClientRegionKind.Caption);
        _positionSaveDebounce?.Cancel();
        _positionSaveDebounce?.Dispose();
        _ = SaveBoundsAsync(CancellationToken.None);
        _viewModel.Shutdown();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task SaveBoundsAsync(CancellationToken cancellationToken)
    {
        try
        {
            PointInt32 position = AppWindow.Position;
            if (_lastSavedPosition is { } last
                && last.X == position.X
                && last.Y == position.Y)
            {
                return;
            }

            var settings = await _settingsStore.LoadAsync(cancellationToken);
            settings.Width = FloatingWindowDefaults.FixedSize;
            settings.Height = FloatingWindowDefaults.FixedSize;
            settings.X = position.X;
            settings.Y = position.Y;
            try
            {
                settings.LastDisplayId = _displayAreas
                    .GetContaining(new PixelRect(
                        position.X,
                        position.Y,
                        GetPhysicalFixedSize(),
                        GetPhysicalFixedSize()))
                    .DisplayId;
            }
            catch (Exception)
            {
                // Coordinate persistence remains valid even if monitor metadata is unavailable.
                settings.LastDisplayId = null;
            }
            await _settingsStore.SaveAsync(settings, cancellationToken);
            _lastSavedPosition = position;
            _positionSaveCount++;
        }
        catch (Exception ex)
        {
            _log.Error($"保存悬浮窗位置失败: {ex.GetType().Name}");
        }
    }
}
