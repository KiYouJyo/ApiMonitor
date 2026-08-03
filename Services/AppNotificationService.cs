using ApiMonitor.Models;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace ApiMonitor.Services;

/// <summary>基于 Microsoft.Windows.AppNotifications.AppNotificationManager 的实现。</summary>
public sealed class AppNotificationService : IAppNotificationService
{
    private readonly AppLog? _log;
    private IAppStrings? _strings;
    private readonly List<NotificationActivationPayload> _pendingInitialPayloads = new();
    private bool _registered;

    public AppNotificationService(AppLog? log = null, IAppStrings? strings = null)
    {
        _log = log;
        _strings = strings;
    }

    public event EventHandler<NotificationActivationPayload>? Activated;

    public bool IsRegistered => _registered;

    public void Register()
    {
        if (_registered)
        {
            return;
        }

        try
        {
            // 顺序要求：先绑定 NotificationInvoked，再调用 Register。
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            // 注册失败不阻塞应用（通知功能不可用但应用仍可运行）。
            _log?.Error($"AppNotification 注册失败: {ex.GetType().Name}");
        }
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception ex)
        {
            _log?.Error($"AppNotification 注销失败: {ex.GetType().Name}");
        }
        finally
        {
            _registered = false;
        }
    }

    public NotificationActivationPayload? DrainInitialPayload()
    {
        lock (_pendingInitialPayloads)
        {
            if (_pendingInitialPayloads.Count == 0)
            {
                return null;
            }

            var payload = _pendingInitialPayloads[0];
            _pendingInitialPayloads.Clear();
            return payload;
        }
    }

    public void HandleAppInstanceActivation(object? args)
    {
        if (args is not AppActivationArguments activationArgs
            || activationArgs.Kind != ExtendedActivationKind.AppNotification
            || activationArgs.Data is not AppNotificationActivatedEventArgs notificationArgs)
        {
            return;
        }

        RoutePayload(Parse(notificationArgs));
    }

    public void ShowLowBalance(
        string accountId,
        string providerId,
        string providerDisplayName,
        string accountDisplayName,
        IReadOnlyList<LowBalanceNotificationItem> items,
        string tag)
    {
        if (items.Count == 0)
        {
            return;
        }

        var builder = new AppNotificationBuilder()
            .AddArgument("action", NotificationActions.OpenAccount)
            .AddArgument("accountId", accountId)
            .AddArgument("providerId", providerId)
            .AddArgument("metricId", items[0].MetricId)
            .AddText(T("Notification.LowBalanceTitle", "ApiMonitor：余额不足"))
            .AddText($"{providerDisplayName} · {accountDisplayName}");

        foreach (string line in MergeItemLines(items.Select(i => i.ValueText)))
        {
            builder.AddText(line);
        }

        builder
            .AddButton(new AppNotificationButton(T("Notification.OpenAccount", "打开账户"))
                .AddArgument("action", NotificationActions.OpenAccount)
                .AddArgument("accountId", accountId)
                .AddArgument("providerId", providerId)
                .AddArgument("metricId", items[0].MetricId))
            .AddButton(new AppNotificationButton(T("Notification.Snooze24h", "暂停提醒 24 小时"))
                .AddArgument("action", NotificationActions.Snooze24Hours)
                .AddArgument("accountId", accountId)
                .AddArgument("providerId", providerId)
                .AddArgument("metricId", items[0].MetricId))
            .SetTag(tag)
            .SetGroup(NotificationTags.Group)
            .SetScenario(AppNotificationScenario.Default);

        ShowSafely(builder.BuildNotification());
    }

    public void ShowRecovery(
        string accountId,
        string providerId,
        string providerDisplayName,
        string accountDisplayName,
        IReadOnlyList<RecoveryNotificationItem> items,
        string tag)
    {
        if (items.Count == 0)
        {
            return;
        }

        var builder = new AppNotificationBuilder()
            .AddArgument("action", NotificationActions.OpenAccount)
            .AddArgument("accountId", accountId)
            .AddArgument("providerId", providerId)
            .AddArgument("metricId", items[0].MetricId)
            .AddText(T("Notification.RecoveredTitle", "ApiMonitor：余额已恢复"))
            .AddText($"{providerDisplayName} · {accountDisplayName}");

        foreach (string line in MergeItemLines(items.Select(i => i.ValueText)))
        {
            builder.AddText(line);
        }

        builder
            .SetTag(tag)
            .SetGroup(NotificationTags.Group)
            .SetScenario(AppNotificationScenario.Default);

        ShowSafely(builder.BuildNotification());
    }

    public void ShowTestNotification()
    {
        var notification = new AppNotificationBuilder()
            .AddArgument("action", NotificationActions.Test)
            .AddText(T("Notification.TestTitle", "ApiMonitor：测试通知"))
            .AddText(T("Notification.TestBody", "这是一条测试通知。点击后打开 ApiMonitor。"))
            .SetTag("ApiMonitor-test")
            .SetGroup(NotificationTags.Group)
            .SetScenario(AppNotificationScenario.Default)
            .BuildNotification();

        ShowSafely(notification);
    }

    public void RemoveAccountNotifications(string accountId)
    {
        try
        {
            AppNotificationManager.Default
                .RemoveByTagAsync(NotificationTags.AccountTag(accountId))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _log?.Error($"移除账户通知失败: {ex.GetType().Name}");
        }
    }

    /// <summary>v0.6.0：注入字符串服务（Program 中先于 CompositionRoot 创建，稍后注入）。</summary>
    public void SetStrings(IAppStrings strings) =>
        _strings ??= strings;

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        RoutePayload(Parse(args));
    }

    private void RoutePayload(NotificationActivationPayload payload)
    {
        if (!payload.IsValid)
        {
            return;
        }

        lock (_pendingInitialPayloads)
        {
            bool hasSubscribers = Activated is not null;
            if (!hasSubscribers)
            {
                // 应用尚未完成初始化：暂存，由 App 在启动流程中取出。
                _pendingInitialPayloads.Add(payload);
                return;
            }
        }

        Activated?.Invoke(this, payload);
    }

    private static NotificationActivationPayload Parse(AppNotificationActivatedEventArgs args)
    {
        string action = args.Arguments.TryGetValue("action", out var a) ? a : string.Empty;
        args.Arguments.TryGetValue("accountId", out var accountId);
        args.Arguments.TryGetValue("providerId", out var providerId);
        args.Arguments.TryGetValue("metricId", out var metricId);
        return new NotificationActivationPayload(action, accountId, providerId, metricId);
    }

    private void ShowSafely(AppNotification notification)
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            // 通知发送失败不影响余额保存。
            _log?.Error($"发送通知失败: {ex.GetType().Name}");
        }
    }

    /// <summary>合并同一账户多条指标：最多展示前 3 条，超出提示其余条数。</summary>
    private static IReadOnlyList<string> MergeItemLines(IEnumerable<string> lines)
    {
        var list = lines.ToList();
        if (list.Count <= 3)
        {
            return list;
        }

        var result = list.Take(3).ToList();
        result.Add($"另有 {list.Count - 3} 项余额低于阈值");
        return result;
    }

    private string T(string key, string fallback) =>
        _strings is null ? fallback : _strings.Get(key);
}
