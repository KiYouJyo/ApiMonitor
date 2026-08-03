using ApiMonitor.Helpers;
using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>账户卡片视图模型，持有刷新/复制/编辑/删除/历史命令与展示状态。</summary>
public sealed partial class AccountListItemViewModel : ObservableObject
{
    private readonly Func<Task> _refreshAsync;
    private readonly Func<Task> _editAsync;
    private readonly Func<Task> _deleteAsync;
    private readonly Func<Task> _copyAsync;
    private readonly Func<Task> _historyAsync;

    private IReadOnlyList<BalanceMetric> _latestMetrics = Array.Empty<BalanceMetric>();

    public ApiAccount Account { get; }

    public string ProviderDisplayName { get; }

    public string DisplayName => Account.DisplayName;

    public bool HasStoredCredential => Account.HasCredential;

    /// <summary>当前最新指标（供编辑对话框设置阈值）。</summary>
    public IReadOnlyList<BalanceMetric> LatestMetricsForEditor => _latestMetrics;

    public bool AutoRefreshEnabled => Account.Monitoring.AutoRefreshEnabled;

    public string AutoRefreshStatusText => AutoRefreshEnabled ? "自动刷新：已开启" : "自动刷新：已关闭";

    public string RefreshIntervalText => $"刷新间隔：{Account.Monitoring.RefreshIntervalMinutes} 分钟";

    public string NextRefreshText
    {
        get
        {
            if (!AutoRefreshEnabled)
            {
                return "下次刷新：自动刷新已关闭";
            }

            return Account.Monitoring.NextRefreshAtUtc is { } next
                ? "下次刷新：" + FormatTime(next)
                : "下次刷新：尚未查询";
        }
    }

    public string ThresholdSummaryText { get; private set; } = "尚无余额数据";

    public bool IsLowBalance { get; private set; }

    /// <summary>当前状态分类（正常/低余额/未知/失败），由快照与最近错误派生。</summary>
    public AccountStatusKind StatusKind { get; private set; } = AccountStatusKind.Unknown;

    public string StatusKindText => StatusKind switch
    {
        AccountStatusKind.Normal => "正常",
        AccountStatusKind.Low => "低余额",
        AccountStatusKind.Failed => "失败",
        _ => "未知",
    };

    /// <summary>通知激活定位时的高亮标记（由主窗口清除）。</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>OpenRouter 凭据模式文本（DeepSeek 为空）。</summary>
    public string CredentialModeText =>
        Account.ProviderId == "openrouter"
            ? string.Equals(Account.CredentialMode, "management-key", StringComparison.OrdinalIgnoreCase)
                ? "Management Key"
                : "普通 API Key"
            : string.Empty;

    public bool HasCredentialModeText => !string.IsNullOrEmpty(CredentialModeText);

    /// <summary>该账户通知开关摘要（三态：开启/关闭/继承全局）。</summary>
    public string NotificationsEnabledText => Account.Notification.NotificationsEnabled switch
    {
        true => "通知：开启",
        false => "通知：关闭",
        _ => "通知：继承全局",
    };

    /// <summary>暂停提醒摘要（由通知状态读取，非持久化账户字段）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnooze))]
    private string _snoozeSummaryText = string.Empty;

    public bool HasSnooze => !string.IsNullOrEmpty(SnoozeSummaryText);

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _isCopying;

    [ObservableProperty]
    private bool _isHistoryOpen;

    [ObservableProperty]
    private bool _isAvailable;

    [ObservableProperty]
    private bool _hasSnapshot;

    [ObservableProperty]
    private string _availabilityText = "不可用";

    [ObservableProperty]
    private string _lastSuccessText = "尚未成功更新";

    /// <summary>账户卡片“最近成功更新”整行文本（含前缀）。</summary>
    public string LastSuccessLine => $"最近成功更新：{LastSuccessText}";

    [ObservableProperty]
    private string _lastErrorText = string.Empty;

    [ObservableProperty]
    private bool _hasLastError;

    [ObservableProperty]
    private IReadOnlyList<BalanceLine> _balanceLines = Array.Empty<BalanceLine>();

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand EditCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public IAsyncRelayCommand CopyKeyCommand { get; }

    public IAsyncRelayCommand HistoryCommand { get; }

    public AccountListItemViewModel(
        ApiAccount account,
        string providerDisplayName,
        AccountBalanceRecord? record,
        Func<Task> refreshAsync,
        Func<Task> editAsync,
        Func<Task> deleteAsync,
        Func<Task> copyAsync,
        Func<Task> historyAsync)
    {
        Account = account;
        ProviderDisplayName = providerDisplayName;
        _refreshAsync = refreshAsync;
        _editAsync = editAsync;
        _deleteAsync = deleteAsync;
        _copyAsync = copyAsync;
        _historyAsync = historyAsync;

        AvailabilityText = "不可用";
        LastSuccessText = "尚未成功更新";
        LastErrorText = string.Empty;
        BalanceLines = Array.Empty<BalanceLine>();

        RefreshCommand = new AsyncRelayCommand(
            () => _refreshAsync(),
            () => !IsRefreshing);
        EditCommand = new AsyncRelayCommand(() => _editAsync());
        DeleteCommand = new AsyncRelayCommand(() => _deleteAsync());
        CopyKeyCommand = new AsyncRelayCommand(
            () => _copyAsync(),
            () => !IsCopying && Account.HasCredential);
        HistoryCommand = new AsyncRelayCommand(
            () => _historyAsync(),
            () => !IsHistoryOpen);

        if (record?.LastSuccessfulSnapshot is { } snapshot)
        {
            ApplySnapshot(snapshot);
        }
        else if (record?.LastQuerySuccessAt is { } lastSuccess)
        {
            LastSuccessText = FormatTime(lastSuccess);
        }

        if (record?.LastQueryAttemptAt is not null && record.LastSuccessfulSnapshot is null)
        {
            LastErrorText = "最近一次查询未成功";
        }

        RecomputeStatusKind();
    }

    partial void OnIsRefreshingChanged(bool value) =>
        RefreshCommand.NotifyCanExecuteChanged();

    partial void OnIsCopyingChanged(bool value) =>
        CopyKeyCommand.NotifyCanExecuteChanged();

    partial void OnIsHistoryOpenChanged(bool value) =>
        HistoryCommand.NotifyCanExecuteChanged();

    partial void OnLastSuccessTextChanged(string value) =>
        OnPropertyChanged(nameof(LastSuccessLine));

    partial void OnLastErrorTextChanged(string value)
    {
        HasLastError = !string.IsNullOrEmpty(value);
        RecomputeStatusKind();
    }

    public void ApplySnapshot(BalanceSnapshot snapshot)
    {
        IsAvailable = snapshot.IsAvailable;
        HasSnapshot = true;
        AvailabilityText = snapshot.IsAvailable ? "可用" : "不可用";
        LastSuccessText = FormatTime(snapshot.RetrievedAt);
        LastErrorText = string.Empty;
        _latestMetrics = snapshot.Metrics;
        BalanceLines = snapshot.Metrics
            .Select(b => new BalanceLine(b))
            .ToList();
        RecomputeThresholdSummary();
        RecomputeStatusKind();
    }

    public void ApplyError(BalanceQueryError? error)
    {
        LastErrorText = error?.Message ?? "查询失败。";
        if (!HasSnapshot)
        {
            AvailabilityText = "不可用";
        }

        RecomputeStatusKind();
    }

    /// <summary>查询完成后从最新账户/记录状态刷新卡片显示（含监控与阈值）。</summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(AutoRefreshStatusText));
        OnPropertyChanged(nameof(RefreshIntervalText));
        OnPropertyChanged(nameof(NextRefreshText));
        RecomputeThresholdSummary();
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(LastSuccessLine));
        RecomputeStatusKind();
    }

    private void RecomputeStatusKind()
    {
        StatusKind = !string.IsNullOrEmpty(LastErrorText)
            ? AccountStatusKind.Failed
            : !HasSnapshot
                ? AccountStatusKind.Unknown
                : IsLowBalance
                    ? AccountStatusKind.Low
                    : AccountStatusKind.Normal;
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(StatusKindText));
    }

    private void RecomputeThresholdSummary()
    {
        if (!HasSnapshot || _latestMetrics.Count == 0)
        {
            ThresholdSummaryText = "尚无余额数据";
            IsLowBalance = false;
            OnPropertyChanged(nameof(ThresholdSummaryText));
            OnPropertyChanged(nameof(IsLowBalance));
            return;
        }

        var rules = Account.Monitoring.Thresholds;
        var below = new List<(string DisplayName, decimal Threshold)>();

        foreach (var metric in _latestMetrics)
        {
            var rule = rules.FirstOrDefault(r => r.MetricId == metric.MetricId);
            if (ThresholdEvaluator.Evaluate(metric, rule) == ThresholdStatus.BelowThreshold)
            {
                below.Add((metric.DisplayName, rule!.ThresholdAmount));
            }
        }

        if (below.Count == 0)
        {
            bool anyEnabledRule = rules.Any(r =>
                r.IsEnabled && _latestMetrics.Any(b => b.MetricId == r.MetricId));
            ThresholdSummaryText = anyEnabledRule ? "余额正常" : "未启用提醒";
            IsLowBalance = false;
        }
        else if (below.Count == 1)
        {
            ThresholdSummaryText =
                $"{below[0].DisplayName} 低于阈值 {BalanceFormatter.Format(below[0].Threshold)}";
            IsLowBalance = true;
        }
        else
        {
            ThresholdSummaryText = $"{below.Count} 个指标低于阈值";
            IsLowBalance = true;
        }

        OnPropertyChanged(nameof(ThresholdSummaryText));
        OnPropertyChanged(nameof(IsLowBalance));
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
