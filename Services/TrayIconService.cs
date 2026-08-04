using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 通知区域图标服务实现。
/// 生命周期状态机：Inactive → Active（或 Failed）→ Deleted，显式退出前保持 Active。
/// 左键单击延迟去抖，双击不产生两次打开；右键弹出原生菜单（每次打开前重新计算状态）；
/// Explorer 重启（TaskbarCreated）时重新添加图标，不重复注册事件、不重启进程。
/// 所有回调转发到 UI 线程执行，不在原生消息线程同步等待网络请求。
/// </summary>
public sealed class TrayIconService : ITrayIconService
{
    private static readonly TimeSpan SingleClickDelay = TimeSpan.FromMilliseconds(250);

    private enum LifecycleState
    {
        Inactive,
        Active,
        Failed,
        Deleted,
    }

    private readonly ITrayNativeHost _host;
    private readonly ITrayStatusProvider _statusProvider;
    private readonly ITrayMenuService _menuService;
    private readonly IAccountManager _accountManager;
    private readonly IFloatingWindowService _floatingWindowService;
    private readonly IStartupTaskService _startupTaskService;
    private readonly ITraySettingsStore _settingsStore;
    private readonly Action _exitApplication;
    private readonly AppLog _log;
    private readonly Action _showMainWindow;
    private readonly CancellationTokenSource _lifetime = new();

    private LifecycleState _state = LifecycleState.Inactive;
    private bool _exiting;
    private bool _pendingSingleClick;
    private bool _isRefreshingAll;
    private bool _hasAccounts;
    private bool _hasAutoRefreshEnabled;
    private TrayStatusSnapshot _lastStatus = new(
        TrayStatusText.TooltipFor(0, hasAnySnapshot: false, isRefreshing: false, hasRecentFailure: false),
        IsRefreshing: false,
        HasRecentFailure: false,
        HasAnySnapshot: false,
        LowBalanceRuleCount: 0);

    public TrayIconService(
        ITrayNativeHost host,
        ITrayStatusProvider statusProvider,
        ITrayMenuService menuService,
        IAccountManager accountManager,
        IFloatingWindowService floatingWindowService,
        IStartupTaskService startupTaskService,
        ITraySettingsStore settingsStore,
        Action exitApplication,
        AppLog log,
        Action showMainWindow)
    {
        _host = host;
        _statusProvider = statusProvider;
        _menuService = menuService;
        _accountManager = accountManager;
        _floatingWindowService = floatingWindowService;
        _startupTaskService = startupTaskService;
        _settingsStore = settingsStore;
        _exitApplication = exitApplication;
        _log = log;
        _showMainWindow = showMainWindow;
    }

    public bool IsActive => _state == LifecycleState.Active;

    public bool Initialize()
    {
        if (_exiting)
        {
            return false;
        }

        if (_state == LifecycleState.Active)
        {
            // 幂等：已有图标时不再重复添加。
            return true;
        }

        if (!_host.Initialize())
        {
            _state = LifecycleState.Failed;
            _log.Error("托盘消息窗口初始化失败。");
            return false;
        }

        if (!_host.AddIcon(_lastStatus.TooltipText))
        {
            _state = LifecycleState.Failed;
            _log.Error("托盘图标添加失败。");
            return false;
        }

        _state = LifecycleState.Active;
        _log.Info("托盘图标已添加。");

        // 事件只在此处注册一次；TaskbarCreated 恢复不重复注册。
        _host.LeftClick += OnLeftClick;
        _host.LeftDoubleClick += OnLeftDoubleClick;
        _host.ContextMenuRequested += OnContextMenuRequested;
        _host.TaskbarCreated += OnTaskbarCreated;
        _accountManager.RefreshCompleted += OnAccountRefreshCompleted;
        _accountManager.AccountsChanged += OnAccountsChanged;

        _ = RefreshMenuStateCoreAsync();
        UpdateTooltip();
        return true;
    }

    public void UpdateTooltip()
    {
        if (_exiting)
        {
            return;
        }

        _ = UpdateTooltipCoreAsync();
    }

    public void Shutdown()
    {
        if (_state == LifecycleState.Deleted)
        {
            return;
        }

        _exiting = true;
        bool wasActive = _state == LifecycleState.Active;
        _state = LifecycleState.Deleted;
        _lifetime.Cancel();

        _host.LeftClick -= OnLeftClick;
        _host.LeftDoubleClick -= OnLeftDoubleClick;
        _host.ContextMenuRequested -= OnContextMenuRequested;
        _host.TaskbarCreated -= OnTaskbarCreated;
        _accountManager.RefreshCompleted -= OnAccountRefreshCompleted;
        _accountManager.AccountsChanged -= OnAccountsChanged;

        if (wasActive)
        {
            _host.DeleteIcon();
        }

        _host.Dispose();
        _log.Info("托盘图标已删除。");
    }

    // ------------------------------------------------------------------
    // 回调
    // ------------------------------------------------------------------
    private void OnLeftClick()
    {
        if (_exiting)
        {
            return;
        }

        // 延迟去抖：双击到来时取消挂起的单击，避免“打开-隐藏”冲突。
        _pendingSingleClick = true;
        _ = SingleClickCoreAsync();
    }

    private async Task SingleClickCoreAsync()
    {
        try
        {
            await Task.Delay(SingleClickDelay, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_exiting || !_pendingSingleClick)
        {
            return;
        }

        _pendingSingleClick = false;
        ShowMainWindow();
    }

    private void OnLeftDoubleClick()
    {
        if (_exiting)
        {
            return;
        }

        _pendingSingleClick = false;
        ShowMainWindow();
    }

    private void OnContextMenuRequested(TrayContextMenuRequest request)
    {
        if (_exiting)
        {
            return;
        }

        var items = _menuService.BuildMenu(BuildMenuContext());
        uint? command = _host.ShowContextMenu(items, request);
        if (command is null)
        {
            return;
        }

        RouteCommand((TrayCommand)command);
    }

    private void OnTaskbarCreated()
    {
        if (_exiting)
        {
            return;
        }

        // Explorer 重启恢复：重新添加图标并重设版本，不重复注册事件，不重启进程。
        bool ok = _host.AddIcon(_lastStatus.TooltipText);
        if (ok)
        {
            _state = LifecycleState.Active;
            _log.Info("Explorer 重启后托盘图标已恢复。");
        }
        else
        {
            // 恢复失败只记录错误，允许后续 TaskbarCreated 再次恢复。
            _log.Error("Explorer 重启后托盘图标恢复失败，将在下次 TaskbarCreated 时重试。");
        }
    }

    private void OnAccountRefreshCompleted(object? sender, AccountRefreshCompletedEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        UpdateTooltip();
    }

    private void OnAccountsChanged(object? sender, EventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        _ = RefreshMenuStateCoreAsync();
        UpdateTooltip();
    }

    // ------------------------------------------------------------------
    // 菜单
    // ------------------------------------------------------------------
    private TrayMenuContext BuildMenuContext()
    {
        string autoRefreshText = _hasAutoRefreshEnabled
            ? TrayStatusText.AutoRefreshRunning
            : TrayStatusText.AutoRefreshStopped;

        string lowBalanceText = !_lastStatus.HasAnySnapshot
            ? TrayStatusText.LowBalanceUnknown
            : _lastStatus.LowBalanceRuleCount > 0
                ? TrayStatusText.LowBalanceSummary(_lastStatus.LowBalanceRuleCount)
                : TrayStatusText.LowBalanceNormal;

        StartupTaskStatus? startupStatus = _startupTaskService.CachedStatus;
        bool startWithWindowsChecked = startupStatus is
            StartupTaskStatus.Enabled or StartupTaskStatus.EnabledByPolicy;
        bool startWithWindowsEnabled = startupStatus is not
            (StartupTaskStatus.DisabledByPolicy or StartupTaskStatus.Unknown or null);

        return new TrayMenuContext(
            HasAccounts: _hasAccounts,
            IsRefreshingAll: _isRefreshingAll,
            AutoRefreshStatusText: autoRefreshText,
            LowBalanceStatusText: lowBalanceText,
            StartWithWindowsChecked: startWithWindowsChecked,
            StartWithWindowsEnabled: startWithWindowsEnabled);
    }

    private async Task RefreshMenuStateCoreAsync()
    {
        try
        {
            var accounts = await _accountManager.GetAllAccountsAsync(_lifetime.Token);
            _hasAccounts = accounts.Count > 0;
            _hasAutoRefreshEnabled = accounts.Any(a => a.Monitoring.AutoRefreshEnabled);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"刷新托盘菜单状态失败: {ex.GetType().Name}");
        }
    }

    private async Task UpdateTooltipCoreAsync()
    {
        try
        {
            _lastStatus = await _statusProvider.GetStatusAsync(_lifetime.Token);
            _host.UpdateTip(_lastStatus.TooltipText);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // Tooltip 更新失败不影响主界面。
            _log.Error($"更新托盘 Tooltip 失败: {ex.GetType().Name}");
        }
    }

    // ------------------------------------------------------------------
    // 命令路由
    // ------------------------------------------------------------------
    private void RouteCommand(TrayCommand command)
    {
        if (_exiting)
        {
            return;
        }

        switch (command)
        {
            case TrayCommand.OpenMainWindow:
                ShowMainWindow();
                break;

            case TrayCommand.OpenFloatingWindow:
                _floatingWindowService.Show();
                break;

            case TrayCommand.RefreshAll:
                _ = RefreshAllAsync();
                break;

            case TrayCommand.ToggleStartWithWindows:
                _ = ToggleStartWithWindowsAsync();
                break;

            case TrayCommand.ExitApplication:
                _exitApplication();
                break;
        }
    }

    private void ShowMainWindow()
    {
        try
        {
            _showMainWindow();
        }
        catch (Exception ex)
        {
            _log.Error($"显示主窗口失败: {ex.GetType().Name}");
        }
    }

    /// <summary>刷新全部账户：复用账户级并发锁，正在刷新的账户自动跳过。</summary>
    private async Task RefreshAllAsync()
    {
        if (_isRefreshingAll || _exiting)
        {
            return;
        }

        _isRefreshingAll = true;
        try
        {
            await _accountManager.RefreshAllAccountsAsync(
                BalanceQuerySource.Manual,
                _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"刷新全部账户失败: {ex.GetType().Name}");
        }
        finally
        {
            _isRefreshingAll = false;
            UpdateTooltip();
        }
    }

    private async Task ToggleStartWithWindowsAsync()
    {
        if (_exiting)
        {
            return;
        }

        var current = _startupTaskService.CachedStatus;
        bool isEnabled = current is StartupTaskStatus.Enabled or StartupTaskStatus.EnabledByPolicy;
        var after = isEnabled
            ? await _startupTaskService.DisableAsync(_lifetime.Token)
            : await _startupTaskService.EnableAsync(_lifetime.Token);

        // 保存 UI 偏好，不覆盖系统权威状态。
        try
        {
            var settings = await _settingsStore.LoadAsync(_lifetime.Token);
            settings.StartWithWindows = after is
                StartupTaskStatus.Enabled or StartupTaskStatus.EnabledByPolicy;
            settings.LastKnownStartupTaskState = after;
            await _settingsStore.SaveAsync(settings, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"保存登录启动偏好失败: {ex.GetType().Name}");
        }

        UpdateTooltip();
    }
}
