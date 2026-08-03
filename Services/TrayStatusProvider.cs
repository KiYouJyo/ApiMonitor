using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Services;

/// <summary>
/// 托盘当前状态摘要（Tooltip 与菜单“低余额/自动刷新”状态共用）。
/// </summary>
public sealed record TrayStatusSnapshot(
    string TooltipText,
    bool IsRefreshing,
    bool HasRecentFailure,
    bool HasAnySnapshot,
    int LowBalanceRuleCount);

/// <summary>
/// 基于 IAccountManager 与现有 ThresholdEvaluator 计算托盘状态摘要。
/// 不复制第二套阈值判断逻辑；不接触 API Key。
/// </summary>
public interface ITrayStatusProvider
{
    Task<TrayStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>托盘状态文本的常量与格式化（独立便于测试）。</summary>
public static class TrayStatusText
{
    public const string AppName = "ApiMonitor";
    public static string Normal => L10n.Get("Card.BalanceNormal");
    public static string NoData => L10n.Get("Card.NoBalanceData");
    public static string Refreshing => L10n.Get("Tray.Refreshing");
    public static string RecentFailure => L10n.Get("Tray.RecentFailure");
    public static string AutoRefreshRunning => L10n.Get("Tray.AutoRefreshRunning");
    public static string AutoRefreshStopped => L10n.Get("Tray.AutoRefreshStopped");
    public static string LowBalanceNormal => L10n.Get("Tray.LowBalanceNormal");
    public static string LowBalanceUnknown => L10n.Get("Tray.LowBalanceUnknown");

    public static string LowBalanceSummary(int ruleCount) =>
        L10n.Format("Tray.LowBalanceCountFormat", ruleCount);

    public static string TooltipFor(int lowBalanceRuleCount, bool hasAnySnapshot, bool isRefreshing, bool hasRecentFailure)
    {
        string state = !hasAnySnapshot
            ? NoData
            : lowBalanceRuleCount > 0
                ? L10n.Format("Tray.MetricsBelowThresholdFormat", lowBalanceRuleCount)
                : Normal;

        if (isRefreshing)
        {
            return $"{AppName} — {state}；{Refreshing}";
        }

        if (hasRecentFailure)
        {
            return $"{AppName} — {state}；{RecentFailure}";
        }

        return $"{AppName} — {state}";
    }
}

/// <summary>
/// 默认状态提供者：遍历账户与最近成功快照，复用 ThresholdEvaluator 统计低余额规则数。
/// “正在刷新”与“最近失败”信息从账户刷新事件与记录时间戳推断。
/// </summary>
public sealed class TrayStatusProvider : ITrayStatusProvider
{
    private readonly IAccountManager _accountManager;
    private readonly AppLog? _log;

    public TrayStatusProvider(IAccountManager accountManager, AppLog? log = null)
    {
        _accountManager = accountManager;
        _log = log;
    }

    public async Task<TrayStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var accounts = await _accountManager.GetAllAccountsAsync(cancellationToken);

            bool hasAnySnapshot = false;
            int lowBalanceRuleCount = 0;
            bool hasRecentFailure = false;

            foreach (var account in accounts)
            {
                var record = await _accountManager.GetRecordAsync(account.AccountId, cancellationToken);
                var snapshot = record?.LastSuccessfulSnapshot;
                if (snapshot is not null)
                {
                    hasAnySnapshot = true;
                }

                foreach (var rule in account.Monitoring.Thresholds)
                {
                    var latest = snapshot?.Metrics.FirstOrDefault(m =>
                        string.Equals(m.MetricId, rule.MetricId, StringComparison.OrdinalIgnoreCase));
                    var status = ThresholdEvaluator.Evaluate(latest, rule);
                    if (status == ThresholdStatus.BelowThreshold)
                    {
                        lowBalanceRuleCount++;
                    }
                }

                if (record is not null
                    && record.LastQuerySuccessAt is not null
                    && record.LastQueryAttemptAt is not null
                    && record.LastQueryAttemptAt > record.LastQuerySuccessAt)
                {
                    hasRecentFailure = true;
                }
            }

            bool isRefreshing = _accountManager.HasActiveRefresh;

            string tooltip = TrayStatusText.TooltipFor(
                lowBalanceRuleCount,
                hasAnySnapshot,
                isRefreshing,
                hasRecentFailure);

            return new TrayStatusSnapshot(
                tooltip,
                isRefreshing,
                hasRecentFailure,
                hasAnySnapshot,
                lowBalanceRuleCount);
        }
        catch (Exception ex)
        {
            // 状态计算失败不影响主界面：返回最保守的“无数据”文本。
            _log?.Error($"托盘状态计算失败: {ex.GetType().Name}");
            return new TrayStatusSnapshot(
                TrayStatusText.TooltipFor(0, hasAnySnapshot: false, isRefreshing: false, hasRecentFailure: false),
                IsRefreshing: false,
                HasRecentFailure: false,
                HasAnySnapshot: false,
                LowBalanceRuleCount: 0);
        }
    }
}
