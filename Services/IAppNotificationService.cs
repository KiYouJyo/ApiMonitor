using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// Windows 应用通知（AppNotification）的抽象：注册/注销、创建通知、
/// Tag/Group、删除账户相关通知、处理点击与按钮动作。
/// 阈值状态机（NotificationPolicyEvaluator）与 Windows API 调用分离。
/// 通知参数只包含 action/accountId/providerId/metricId 等非敏感标识。
/// </summary>
public interface IAppNotificationService
{
    bool IsRegistered { get; }

    /// <summary>先绑定 NotificationInvoked，再调用 Register（必须在读取激活参数之前）。</summary>
    void Register();

    /// <summary>应用退出时调用；正在退出时忽略新的通知动作。</summary>
    void Unregister();

    /// <summary>取出初始激活时收到的通知参数（冷启动通知点击 / COM 激活路径）。</summary>
    NotificationActivationPayload? DrainInitialPayload();

    /// <summary>处理 AppInstance 转发来的通知激活（第二实例重定向 / 运行中点击）。</summary>
    void HandleAppInstanceActivation(object? args);

    /// <summary>低余额通知（合并后每个账户最多一条，稳定 Tag 替换旧提醒）。</summary>
    void ShowLowBalance(
        string accountId,
        string providerId,
        string providerDisplayName,
        string accountDisplayName,
        IReadOnlyList<LowBalanceNotificationItem> items,
        string tag);

    void ShowRecovery(
        string accountId,
        string providerId,
        string providerDisplayName,
        string accountDisplayName,
        IReadOnlyList<RecoveryNotificationItem> items,
        string tag);

    void ShowTestNotification();

    void RemoveAccountNotifications(string accountId);

    /// <summary>收到通知点击/按钮动作时触发（payload 只含非敏感标识）。</summary>
    event EventHandler<NotificationActivationPayload>? Activated;
}
