using ApiMonitor.Services;
using ApiMonitor.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private bool _boundsRestored;
    private bool _closing;

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
        RootGrid.DataContext = viewModel;

        Activated += OnWindowActivated;
        Closed += OnWindowClosed;

        ApplyPresenter();
    }

    /// <summary>窗口根元素（供主题服务注册；内部仅供 CompositionRoot 使用）。</summary>
    internal Grid RootGridElement => RootGrid;

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
            _ = NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                AppWindow.Position.X,
                AppWindow.Position.Y,
                (int)FloatingWindowDefaults.FixedSize,
                (int)FloatingWindowDefaults.FixedSize,
                NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_FRAMECHANGED);
        }
        catch (Exception ex)
        {
            _log.Error($"设置悬浮窗工具窗口样式失败: {ex.GetType().Name}");
        }
    }

    /// <summary>整个方块都是拖动命中区，不保留额外标题栏或拖动手柄。</summary>
    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(RootGrid).Properties.IsLeftButtonPressed)
        {
            return;
        }

        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            NativeMethods.ReleaseCapture();
            _ = NativeMethods.SendMessageW(
                hwnd,
                NativeMethods.WM_NCLBUTTONDOWN,
                new IntPtr(NativeMethods.HTCAPTION),
                IntPtr.Zero);
            args.Handled = true;
        }
        catch (Exception ex)
        {
            _log.Error($"拖动悬浮窗失败: {ex.GetType().Name}");
        }
    }

    private async Task RestoreBoundsAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(_lifetime.Token);
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
            bounds.Width,
            bounds.Height,
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
        _ = SaveBoundsAsync();
        _viewModel.Shutdown();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task SaveBoundsAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(CancellationToken.None);
            settings.Width = AppWindow.Size.Width;
            settings.Height = AppWindow.Size.Height;
            settings.X = AppWindow.Position.X;
            settings.Y = AppWindow.Position.Y;
            settings.LastDisplayId = _displayAreas
                .GetContaining(new PixelRect(
                    AppWindow.Position.X,
                    AppWindow.Position.Y,
                    AppWindow.Size.Width,
                    AppWindow.Size.Height))
                .DisplayId;
            await _settingsStore.SaveAsync(settings, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Error($"保存悬浮窗位置失败: {ex.GetType().Name}");
        }
    }
}
