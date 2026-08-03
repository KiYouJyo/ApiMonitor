using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>
/// “通知区域与启动”设置区 ViewModel（主界面设置区绑定）。
/// 系统 StartupTask 状态是权威来源：开关反映系统状态，
/// 本地设置只保存 UI 偏好；DisabledByUser/DisabledByPolicy 显示明确提示。
/// </summary>
public sealed partial class TraySettingsViewModel : ObservableObject
{
    private readonly ITraySettingsStore _settingsStore;
    private readonly IStartupTaskService _startupTaskService;
    private readonly IApplicationExitCoordinator _exitCoordinator;
    private readonly AppLog? _log;
    private readonly CancellationTokenSource _lifetime = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloseBehaviorIndex))]
    private MainWindowCloseBehavior _closeBehavior = MainWindowCloseBehavior.HideToTray;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _isStartupTaskBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartupTaskStatusText))]
    private string _startupTaskStatusText = string.Empty;

    /// <summary>是否有需要展示的登录启动状态提示（DisabledByUser/Policy 等）。</summary>
    public bool HasStartupTaskStatusText => !string.IsNullOrEmpty(StartupTaskStatusText);

    /// <summary>RadioButtons 选中索引：0 = 隐藏到通知区域，1 = 退出 ApiMonitor。</summary>
    public int CloseBehaviorIndex
    {
        get => CloseBehavior == MainWindowCloseBehavior.ExitApplication ? 1 : 0;
        set => CloseBehavior = value == 1
            ? MainWindowCloseBehavior.ExitApplication
            : MainWindowCloseBehavior.HideToTray;
    }

    /// <summary>当前托盘驻留状态摘要（设置区展示）。</summary>
    public string TrayResidencyText { get; } = L10n.Get("Tray.ResidencyEnabled");

    public RelayCommand ExitApplicationCommand { get; }

    public TraySettingsViewModel(
        ITraySettingsStore settingsStore,
        IStartupTaskService startupTaskService,
        IApplicationExitCoordinator exitCoordinator,
        AppLog? log = null)
    {
        _settingsStore = settingsStore;
        _startupTaskService = startupTaskService;
        _exitCoordinator = exitCoordinator;
        _log = log;
        ExitApplicationCommand = new RelayCommand(_exitCoordinator.BeginExit, () => !_exitCoordinator.IsExiting);
    }

    /// <summary>应用启动时调用：加载本地偏好并读取系统 StartupTask 状态。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(_lifetime.Token);
            CloseBehavior = settings.MainWindowCloseBehavior;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"加载托盘设置失败: {ex.GetType().Name}");
        }

        await RefreshStartupTaskStatusAsync();
    }

    /// <summary>从系统重新读取 StartupTask 状态（不信任本地布尔值）。</summary>
    public async Task RefreshStartupTaskStatusAsync()
    {
        IsStartupTaskBusy = true;
        try
        {
            var status = await _startupTaskService.RefreshStatusAsync(_lifetime.Token);
            ApplySystemStatus(status);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"读取登录启动状态失败: {ex.GetType().Name}");
        }
        finally
        {
            IsStartupTaskBusy = false;
        }
    }

    partial void OnCloseBehaviorChanged(MainWindowCloseBehavior value)
    {
        OnPropertyChanged(nameof(CloseBehaviorIndex));
        _ = SavePreferenceAsync();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _ = ToggleStartupTaskAsync(value);
    }

    private async Task SavePreferenceAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(_lifetime.Token);
            settings.MainWindowCloseBehavior = CloseBehavior;
            await _settingsStore.SaveAsync(settings, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"保存关闭行为设置失败: {ex.GetType().Name}");
        }
    }

    private async Task ToggleStartupTaskAsync(bool enable)
    {
        if (IsStartupTaskBusy)
        {
            return;
        }

        IsStartupTaskBusy = true;
        try
        {
            var status = enable
                ? await _startupTaskService.EnableAsync(_lifetime.Token)
                : await _startupTaskService.DisableAsync(_lifetime.Token);

            // 本地设置只保存 UI 偏好，不伪造系统启用状态。
            var settings = await _settingsStore.LoadAsync(_lifetime.Token);
            settings.StartWithWindows = status is StartupTaskStatus.Enabled or StartupTaskStatus.EnabledByPolicy;
            settings.LastKnownStartupTaskState = status;
            await _settingsStore.SaveAsync(settings, _lifetime.Token);

            ApplySystemStatus(status);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"切换登录启动失败: {ex.GetType().Name}");
            await RefreshStartupTaskStatusAsync();
        }
        finally
        {
            IsStartupTaskBusy = false;
        }
    }

    private void ApplySystemStatus(StartupTaskStatus status)
    {
        StartWithWindows = status is StartupTaskStatus.Enabled or StartupTaskStatus.EnabledByPolicy;
        StartupTaskStatusText = status switch
        {
            StartupTaskStatus.DisabledByUser =>
                L10n.Get("Tray.StartupDisabledByUser"),
            StartupTaskStatus.DisabledByPolicy =>
                L10n.Get("Tray.StartupDisabledByPolicy"),
            StartupTaskStatus.EnabledByPolicy =>
                L10n.Get("Tray.StartupEnabledByPolicy"),
            _ => string.Empty,
        };
    }
}
