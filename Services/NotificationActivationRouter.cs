namespace ApiMonitor.Services;

/// <summary>
/// 通知激活路由：把通知点击/按钮动作转发给窗口与协调器。
/// 打开账户 → 显示主窗口并定位账户；暂停提醒 → 只写状态不弹窗；
/// 测试 → 打开主窗口；账户已删除 → 打开主界面并显示普通提示，不崩溃。
/// </summary>
public sealed class NotificationActivationRouter
{
    private readonly IAccountManager _accounts;
    private readonly NotificationCoordinator _coordinator;
    private readonly Action _showMainWindow;
    private readonly Action _navigateHome;
    private readonly Action<string> _focusAccount;
    private readonly Action<string, string> _showMessage;

    public NotificationActivationRouter(
        IAccountManager accounts,
        NotificationCoordinator coordinator,
        Action showMainWindow,
        Action navigateHome,
        Action<string> focusAccount,
        Action<string, string> showMessage)
    {
        _accounts = accounts;
        _coordinator = coordinator;
        _showMainWindow = showMainWindow;
        _navigateHome = navigateHome;
        _focusAccount = focusAccount;
        _showMessage = showMessage;
    }

    public async Task HandleAsync(NotificationActivationPayload payload, CancellationToken cancellationToken)
    {
        switch (payload.Action)
        {
            case NotificationActions.Snooze24Hours:
                // 暂停提醒 24 小时：不要求打开主窗口，不清除低余额状态。
                if (!string.IsNullOrWhiteSpace(payload.AccountId))
                {
                    await _coordinator.SnoozeAsync(payload.AccountId, payload.MetricId, cancellationToken);
                }

                break;

            case NotificationActions.Test:
                _showMainWindow();
                break;

            case NotificationActions.OpenAccount:
                _showMainWindow();
                if (string.IsNullOrWhiteSpace(payload.AccountId))
                {
                    break;
                }

                var account = await _accounts.GetAccountAsync(payload.AccountId, cancellationToken);
                if (account is null)
                {
                    // 已删除账户：强制回到主页并显示普通提示，不崩溃。
                    _navigateHome();
                    _showMessage(L10n.Get("Notify.AccountMissingTitle"), L10n.Get("Notify.AccountMissingMessage"));
                }
                else
                {
                    _focusAccount(payload.AccountId);
                }

                break;
        }
    }
}
