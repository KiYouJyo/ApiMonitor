using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 通知协调器：把成功查询的新快照交给纯逻辑评估器，再调用通知服务展示，
/// 并把状态写回持久化存储。负责暂停 24 小时、删除账户清理与测试通知。
/// 不建立第二套阈值计算；不为 OpenRouter 建立第二套自动刷新器。
/// </summary>
public sealed class NotificationCoordinator
{
    private readonly IAccountManager _accounts;
    private readonly INotificationStateStore _stateStore;
    private readonly INotificationSettingsStore _settingsStore;
    private readonly INotificationPolicyEvaluator _evaluator;
    private readonly IAppNotificationService _notifications;
    private readonly AppLog _log;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<NotificationStateEntry> _states = new();

    public NotificationCoordinator(
        IAccountManager accounts,
        INotificationStateStore stateStore,
        INotificationSettingsStore settingsStore,
        INotificationPolicyEvaluator evaluator,
        IAppNotificationService notifications,
        AppLog log,
        TimeProvider? time = null)
    {
        _accounts = accounts;
        _stateStore = stateStore;
        _settingsStore = settingsStore;
        _evaluator = evaluator;
        _notifications = notifications;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>应用启动时加载通知状态（旧快照不会因此触发任何通知）。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _states = (await _stateStore.LoadAsync(cancellationToken)).ToList();
    }

    /// <summary>
    /// 查询完成事件入口：只在成功查询产生新快照时评估；
    /// 查询失败不更新余额状态、不发送任何提醒、不清除上一次通知状态。
    /// </summary>
    public async Task HandleRefreshCompletedAsync(
        AccountRefreshCompletedEventArgs e,
        CancellationToken cancellationToken)
    {
        if (!e.Result.IsSuccess)
        {
            // v0.9.0：地理/GIS 服务账户的失败探测也可能触发健康通知
            // （瞬时错误连续两次后才通知；手动测试失败不会进入此路径）。
            if (e.Result.Error is { } error)
            {
                await HandleFailureAsync(e.AccountId, error, cancellationToken);
            }

            return;
        }

        if (e.Result.Snapshot is not { } snapshot)
        {
            return;
        }

        var account = await _accounts.GetAccountAsync(e.AccountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        var global = await _settingsStore.LoadAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var decision = _evaluator.Evaluate(
                account,
                snapshot,
                global,
                _states,
                _time.GetUtcNow());

            if (decision.NotificationsSuppressed)
            {
                // 全局或账户通知关闭：不发送，也不改动状态。
                return;
            }

            string tag = NotificationTags.AccountTag(account.AccountId);
            string providerName = ProviderDisplayName(account.ProviderId);

            if (decision.ShouldNotifyLow)
            {
                _notifications.ShowLowBalance(
                    account.AccountId,
                    account.ProviderId,
                    providerName,
                    account.DisplayName,
                    decision.LowItems,
                    tag);
                MarkNotified(decision.UpdatedStates, decision.LowItems.Select(i => i.MetricId), tag);
            }

            if (decision.ShouldNotifyRecovery)
            {
                _notifications.ShowRecovery(
                    account.AccountId,
                    account.ProviderId,
                    providerName,
                    account.DisplayName,
                    decision.RecoveryItems,
                    tag);
            }

            if (decision.ShouldNotifyHealth)
            {
                foreach (var item in decision.HealthItems ?? Array.Empty<HealthNotificationItem>())
                {
                    _notifications.ShowHealthNotification(
                        account.AccountId,
                        account.ProviderId,
                        providerName,
                        account.DisplayName,
                        item.Type,
                        item.Message,
                        tag);
                }
            }

            _states = decision.UpdatedStates.ToList();
            await _stateStore.SaveAsync(_states, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 通知/状态保存失败不影响余额保存。
            _log.Error($"通知评估失败: {ex.GetType().Name}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 探测失败（无快照）时评估健康通知。只针对地理/GIS 服务账户；
    /// AI 账户保持 v0.8.0 行为（失败不通知）。
    /// </summary>
    private async Task HandleFailureAsync(
        string accountId,
        BalanceQueryError error,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.GetAccountAsync(accountId, cancellationToken);
        if (account is null || !IsServiceProvider(account.ProviderId))
        {
            return;
        }

        var global = await _settingsStore.LoadAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var decision = _evaluator.EvaluateFailure(
                account,
                error,
                global,
                _states,
                _time.GetUtcNow());

            if (decision.NotificationsSuppressed)
            {
                return;
            }

            string tag = NotificationTags.AccountTag(account.AccountId);
            string providerName = ProviderDisplayName(account.ProviderId);
            foreach (var item in decision.HealthItems ?? Array.Empty<HealthNotificationItem>())
            {
                _notifications.ShowHealthNotification(
                    account.AccountId,
                    account.ProviderId,
                    providerName,
                    account.DisplayName,
                    item.Type,
                    item.Message,
                    tag);
            }

            _states = decision.UpdatedStates.ToList();
            await _stateStore.SaveAsync(_states, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"失败通知评估失败: {ex.GetType().Name}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>“暂停提醒 24 小时”动作：设置该账户该指标的 SnoozedUntil，不打开主窗口。</summary>
    public async Task SnoozeAsync(string accountId, string? metricId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _time.GetUtcNow();
            bool changed = false;
            foreach (var state in _states)
            {
                if (!string.Equals(state.AccountId, accountId, StringComparison.OrdinalIgnoreCase)
                    || (metricId is not null
                        && !string.Equals(state.MetricId, metricId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                state.SnoozedUntil = now.AddHours(24);
                state.LastState = NotificationStateKind.Snoozed;
                changed = true;
            }

            // 尚无状态条目的指标也创建暂停状态（例如通知来自合并提醒）。
            if (!changed && metricId is not null)
            {
                _states.Add(new NotificationStateEntry
                {
                    AccountId = accountId,
                    MetricId = metricId,
                    LastState = NotificationStateKind.Snoozed,
                    SnoozedUntil = now.AddHours(24),
                });
            }

            await _stateStore.SaveAsync(_states, cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Error($"暂停提醒失败: {ex.GetType().Name}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>删除账户：移除该账户的活动通知、通知状态与暂停状态，不动其他账户。</summary>
    public async Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        _notifications.RemoveAccountNotifications(accountId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _states = _states
                .Where(s => !string.Equals(s.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            await _stateStore.DeleteAccountAsync(accountId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>发送测试通知：不查询 API、不改变阈值状态、不写入余额历史。</summary>
    public void ShowTestNotification() => _notifications.ShowTestNotification();

    /// <summary>读取账户当前有效的暂停截止时间（供账户卡片显示摘要）。</summary>
    public Task<DateTimeOffset?> GetSnoozedUntilAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _time.GetUtcNow();
        DateTimeOffset? active = null;
        foreach (var state in _states)
        {
            if (!string.Equals(state.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (state.SnoozedUntil is { } until
                && until > now
                && (active is null || until > active.Value))
            {
                active = until;
            }
        }

        return Task.FromResult(active);
    }

    private void MarkNotified(
        IReadOnlyList<NotificationStateEntry> states,
        IEnumerable<string> metricIds,
        string tag)
    {
        var ids = metricIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var state in states.Where(s => ids.Contains(s.MetricId)))
        {
            state.LastNotificationTag = tag;
        }
    }

    private string ProviderDisplayName(string providerId) =>
        _accounts.Providers.FirstOrDefault(p =>
            string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? providerId;

    private bool IsServiceProvider(string providerId) =>
        _accounts.Providers.Any(p =>
            string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
            && p.EffectiveCategory != ProviderCategory.ArtificialIntelligence);
}
