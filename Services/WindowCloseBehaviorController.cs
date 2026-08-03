using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 主窗口关闭行为控制器（v0.4.0）：
/// HideToTray：取消真正关闭并隐藏窗口，自动刷新与托盘继续；
/// ExitApplication：调用统一退出协调器完成完整退出。
/// 首次隐藏前显示一次说明（用户可选择不再提示，持久化）；
/// 用户选择取消时保持窗口显示。
/// </summary>
public sealed class WindowCloseBehaviorController
{
    private readonly ITraySettingsStore _settingsStore;
    private readonly IDialogService _dialogs;
    private readonly IApplicationExitCoordinator _exitCoordinator;
    private readonly IMainWindowController _window;
    private readonly AppLog _log;
    private bool _hasShownFirstExplanation;

    public WindowCloseBehaviorController(
        ITraySettingsStore settingsStore,
        IDialogService dialogs,
        IApplicationExitCoordinator exitCoordinator,
        IMainWindowController window,
        AppLog log)
    {
        _settingsStore = settingsStore;
        _dialogs = dialogs;
        _exitCoordinator = exitCoordinator;
        _window = window;
        _log = log;
    }

    /// <summary>
    /// 处理关闭请求。返回后调用方应保持窗口状态不变（隐藏/退出由本方法执行）。
    /// 必须在 AppWindow.Closing 已同步取消（args.Cancel = true）之后调用。
    /// </summary>
    public async Task HandleCloseRequestedAsync()
    {
        if (_exitCoordinator.IsExiting)
        {
            // 显式退出流程中：放行真正关闭。
            _window.AllowClose();
            _window.Close();
            return;
        }

        TraySettings settings;
        try
        {
            settings = await _settingsStore.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Error($"读取关闭行为设置失败: {ex.GetType().Name}");
            settings = new TraySettings();
        }

        if (settings.MainWindowCloseBehavior == MainWindowCloseBehavior.ExitApplication)
        {
            _exitCoordinator.BeginExit();
            return;
        }

        // HideToTray：首次说明（只提示一次，除非用户要求再次说明）。
        if (settings.ShowFirstCloseExplanation && !_hasShownFirstExplanation)
        {
            _hasShownFirstExplanation = true;
            var choice = await _dialogs.ShowFirstCloseExplanationAsync(CancellationToken.None);
            switch (choice)
            {
                case FirstCloseChoice.Cancel:
                    return; // 保持窗口显示。

                case FirstCloseChoice.HideAndDontAskAgain:
                    try
                    {
                        settings.ShowFirstCloseExplanation = false;
                        await _settingsStore.SaveAsync(settings, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"保存“不再提示”设置失败: {ex.GetType().Name}");
                    }

                    break;
            }
        }

        try
        {
            _window.Hide();
        }
        catch (Exception ex)
        {
            _log.Error($"隐藏主窗口失败: {ex.GetType().Name}");
        }
    }
}
