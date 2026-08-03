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

    public string AutoRefreshStatusText => AutoRefreshEnabled ? L10n.Get("Card.AutoRefreshOn") : L10n.Get("Card.AutoRefreshOff");

    public string RefreshIntervalText => L10n.Format("Card.RefreshIntervalFormat", Account.Monitoring.RefreshIntervalMinutes);

    public string NextRefreshText
    {
        get
        {
            if (!AutoRefreshEnabled)
            {
                return L10n.Get("Card.NextRefreshOff");
            }

            return Account.Monitoring.NextRefreshAtUtc is { } next
                ? L10n.Format("Card.NextRefreshAtFormat", FormatTime(next))
                : L10n.Get("Card.NextRefreshNever");
        }
    }

    public string ThresholdSummaryText { get; private set; } = L10n.Get("Card.NoBalanceData");

    public bool IsLowBalance { get; private set; }

    /// <summary>当前状态分类（正常/低余额/未知/失败），由快照与最近错误派生。</summary>
    public AccountStatusKind StatusKind { get; private set; } = AccountStatusKind.Unknown;

    public string StatusKindText => StatusKind switch
    {
        AccountStatusKind.Normal => L10n.Get("Home.StatusNormal"),
        AccountStatusKind.Low => L10n.Get("Home.StatusLow"),
        AccountStatusKind.Failed => L10n.Get("Home.StatusFailed"),
        _ => L10n.Get("Home.StatusUnknown"),
    };

    /// <summary>通知激活定位时的高亮标记（由主窗口清除）。</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>OpenRouter 凭据模式文本（DeepSeek 为空）。</summary>
    public string CredentialModeText =>
        Account.ProviderId == "openrouter"
            ? string.Equals(Account.CredentialMode, "management-key", StringComparison.OrdinalIgnoreCase)
                ? "Management Key"
                : L10n.Get("Card.CredentialModeApiKey")
            : string.Empty;

    public bool HasCredentialModeText => !string.IsNullOrEmpty(CredentialModeText);

    /// <summary>该账户通知开关摘要（三态：开启/关闭/继承全局）。</summary>
    public string NotificationsEnabledText => Account.Notification.NotificationsEnabled switch
    {
        true => L10n.Get("Card.NotificationsOn"),
        false => L10n.Get("Card.NotificationsOff"),
        _ => L10n.Get("Card.NotificationsInherit"),
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
    private string _availabilityText = L10n.Get("Card.Unavailable");

    [ObservableProperty]
    private string _lastSuccessText = L10n.Get("Card.NotUpdatedYet");

    /// <summary>账户卡片“最近成功更新”整行文本（含前缀）。</summary>
    public string LastSuccessLine => L10n.Format("Card.LastSuccessLineFormat", LastSuccessText);

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

    /// <summary>v0.6.0：从账户卡片进入数据洞察并预选该账户。</summary>
    public IAsyncRelayCommand ViewTrendsCommand { get; }

    public AccountListItemViewModel(
        ApiAccount account,
        string providerDisplayName,
        AccountBalanceRecord? record,
        Func<Task> refreshAsync,
        Func<Task> editAsync,
        Func<Task> deleteAsync,
        Func<Task> copyAsync,
        Func<Task> historyAsync,
        Func<Task>? viewTrendsAsync = null)
    {
        Account = account;
        ProviderDisplayName = providerDisplayName;
        _refreshAsync = refreshAsync;
        _editAsync = editAsync;
        _deleteAsync = deleteAsync;
        _copyAsync = copyAsync;
        _historyAsync = historyAsync;

        AvailabilityText = L10n.Get("Card.Unavailable");
        LastSuccessText = L10n.Get("Card.NotUpdatedYet");
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
        ViewTrendsCommand = new AsyncRelayCommand(() => viewTrendsAsync?.Invoke() ?? Task.CompletedTask);

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
            LastErrorText = L10n.Get("Card.LastQueryFailed");
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
        AvailabilityText = snapshot.IsAvailable ? L10n.Get("Card.Available") : L10n.Get("Card.Unavailable");
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
        LastErrorText = error?.Message ?? L10n.Get("Card.QueryFailed");
        if (!HasSnapshot)
        {
            AvailabilityText = L10n.Get("Card.Unavailable");
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
            ThresholdSummaryText = L10n.Get("Card.NoBalanceData");
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
            ThresholdSummaryText = anyEnabledRule ? L10n.Get("Card.BalanceNormal") : L10n.Get("Card.AlertsDisabled");
            IsLowBalance = false;
        }
        else if (below.Count == 1)
        {
            ThresholdSummaryText =
                L10n.Format("Card.BelowThresholdFormat", below[0].DisplayName, BalanceFormatter.Format(below[0].Threshold));
            IsLowBalance = true;
        }
        else
        {
            ThresholdSummaryText = L10n.Format("Card.MetricsBelowThresholdFormat", below.Count);
            IsLowBalance = true;
        }

        OnPropertyChanged(nameof(ThresholdSummaryText));
        OnPropertyChanged(nameof(IsLowBalance));
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
