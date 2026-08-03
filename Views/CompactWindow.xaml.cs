using ApiMonitor.Services;
using ApiMonitor.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace ApiMonitor.Views;

/// <summary>
/// 紧凑置顶余额窗口：只做视图职责（置顶、位置/尺寸、事件转发），
/// 业务状态全部来自 CompactWindowViewModel 与账户服务。
/// </summary>
public sealed partial class CompactWindow : Window
{
    private readonly CompactWindowViewModel _viewModel;
    private readonly ICompactWindowSettingsStore _settingsStore;
    private readonly IDisplayAreaProvider _displayAreas;
    private readonly AppLog _log;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _boundsRestored;
    private bool _closing;

    /// <summary>由宿主转发给 WindowManager，用于从紧凑窗口重新打开主窗口。</summary>
    public event EventHandler? OpenMainWindowRequested;

    public CompactWindow(
        CompactWindowViewModel viewModel,
        ICompactWindowSettingsStore settingsStore,
        IDisplayAreaProvider displayAreas,
        AppLog log)
    {
        _viewModel = viewModel;
        _settingsStore = settingsStore;
        _displayAreas = displayAreas;
        _log = log;

        InitializeComponent();
        Title = "ApiMonitor";
        RootGrid.DataContext = viewModel;

        viewModel.AlwaysOnTopChanged += OnAlwaysOnTopChanged;
        viewModel.OpenMainWindowRequested += OnOpenMainWindowRequested;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;

        ApplyAlwaysOnTop(viewModel.IsAlwaysOnTop);
    }

    private void OnOpenMainWindowRequested(object? sender, EventArgs e) =>
        OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_boundsRestored)
        {
            return;
        }

        _boundsRestored = true;
        _ = RestoreBoundsAsync();
        _ = _viewModel.InitializeAsync(_lifetime.Token);
    }

    private void OnAlwaysOnTopChanged(object? sender, EventArgs e) =>
        ApplyAlwaysOnTop(_viewModel.IsAlwaysOnTop);

    private void ApplyAlwaysOnTop(bool isAlwaysOnTop)
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = isAlwaysOnTop;
                presenter.PreferredMinimumWidth = (int)CompactWindowDefaults.MinWidth;
                presenter.PreferredMinimumHeight = (int)CompactWindowDefaults.MinHeight;
            }
        }
        catch (Exception ex)
        {
            // 置顶失败不崩溃；窗口仍可用。
            _log.Error($"设置紧凑窗口置顶失败: {ex.GetType().Name}");
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

            AppWindow.MoveAndResize(new RectInt32(
                restored.X,
                restored.Y,
                restored.Width,
                restored.Height));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // 位置恢复失败不影响启动。
            _log.Error($"恢复紧凑窗口位置失败: {ex.GetType().Name}");
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _viewModel.AlwaysOnTopChanged -= OnAlwaysOnTopChanged;
        _viewModel.OpenMainWindowRequested -= OnOpenMainWindowRequested;
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
            settings.IsAlwaysOnTop = _viewModel.IsAlwaysOnTop;
            settings.SelectedAccountId = _viewModel.SelectedAccount?.AccountId;
            settings.SelectedMetricId = _viewModel.SelectedMetric?.MetricId;
            settings.SelectedCurrency = null;
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
            _log.Error($"保存紧凑窗口位置失败: {ex.GetType().Name}");
        }
    }
}
