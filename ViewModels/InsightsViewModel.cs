using System.Collections.ObjectModel;
using System.Globalization;
using ApiMonitor.Helpers;
using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>数据洞察页账户选项。</summary>
public sealed record InsightsAccountOption(string AccountId, string DisplayName);

/// <summary>数据洞察页指标选项（来自账户通用 BalanceMetric，不写死）。</summary>
public sealed record InsightsMetricOption(
    string MetricId,
    string DisplayName,
    string Unit,
    BalanceMetricKind Kind,
    MetricValueKind ValueKind,
    MetricKind? DetailedKind);

/// <summary>数据洞察页时间范围选项。</summary>
public sealed record InsightsRangeOption(InsightsTimeRange Range, string DisplayName);

/// <summary>历史表格行。</summary>
public sealed class InsightsHistoryRow
{
    public required string TimeText { get; init; }

    public required string ProviderId { get; init; }

    public required string AccountDisplayName { get; init; }

    public required string MetricDisplayName { get; init; }

    public required string ValueText { get; init; }

    public required string Unit { get; init; }

    public required string SourceText { get; init; }
}

/// <summary>
/// v0.6.0：数据洞察页 ViewModel。
/// 按需加载历史（进入页面/切换账户时）；切换账户、指标或范围时取消旧请求；
/// 图表最多绘制约 500 点（TrendDataBuilder 抽样）；不阻塞 UI 线程；
/// 页面离开后释放大型集合（Clear 调用）。
/// </summary>
public sealed partial class InsightsViewModel : ObservableObject
{
    private readonly IAccountManager _accountManager;
    private readonly IInsightsHistoryProvider _historyProvider;
    private readonly ITrendDataBuilder _trendBuilder;
    private readonly IConsumptionEstimateService _estimateService;
    private readonly ICsvHistoryExporter _csvExporter;
    private readonly IFilePickerService _filePicker;
    private readonly AppLog _log;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _queryCts;

    public ObservableCollection<InsightsAccountOption> Accounts { get; } = new();

    public ObservableCollection<InsightsMetricOption> Metrics { get; } = new();

    public ObservableCollection<InsightsRangeOption> Ranges { get; } = new();

    public ObservableCollection<InsightsHistoryRow> HistoryRows { get; } = new();

    /// <summary>趋势点（图表控件绑定）。</summary>
    public ObservableCollection<TrendPoint> TrendPoints { get; } = new();

    [ObservableProperty]
    private InsightsAccountOption? _selectedAccount;

    [ObservableProperty]
    private InsightsMetricOption? _selectedMetric;

    [ObservableProperty]
    private InsightsRangeOption _selectedRange;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string _emptyMessage = string.Empty;

    [ObservableProperty]
    private string _currentValueText = "—";

    [ObservableProperty]
    private string _changeInRangeText = "—";

    [ObservableProperty]
    private string _firstValueText = "—";

    [ObservableProperty]
    private string _latestValueText = "—";

    [ObservableProperty]
    private string _minimumValueText = "—";

    [ObservableProperty]
    private string _maximumValueText = "—";

    [ObservableProperty]
    private string _dailyConsumptionText = "—";

    [ObservableProperty]
    private string _estimatedDaysLeftText = "—";

    [ObservableProperty]
    private string _estimateExplanationText = string.Empty;

    /// <summary>v0.9.0：服务账户成功率（健康探测数 / 探测总数）。</summary>
    [ObservableProperty]
    private string _successRateText = string.Empty;

    [ObservableProperty]
    private bool _hasSuccessRate;

    /// <summary>图表可访问摘要（AutomationProperties.Name 绑定）。</summary>
    [ObservableProperty]
    private string _chartSummaryText = string.Empty;

    [ObservableProperty]
    private bool _hasChartSummary;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ExportCsvCommand { get; }

    private IReadOnlyList<BalanceHistoryEntry> _loadedHistory = Array.Empty<BalanceHistoryEntry>();

    /// <summary>数据洞察页当前目标账户（由账户卡片“查看趋势”入口设置）。</summary>
    public string? TargetAccountId { get; set; }

    public InsightsViewModel(
        IAccountManager accountManager,
        IInsightsHistoryProvider historyProvider,
        ITrendDataBuilder trendBuilder,
        IConsumptionEstimateService estimateService,
        ICsvHistoryExporter csvExporter,
        IFilePickerService filePicker,
        AppLog? log = null)
    {
        _accountManager = accountManager;
        _historyProvider = historyProvider;
        _trendBuilder = trendBuilder;
        _estimateService = estimateService;
        _csvExporter = csvExporter;
        _filePicker = filePicker;
        _log = log ?? new AppLog(Path.GetTempPath());

        Ranges.Add(new InsightsRangeOption(InsightsTimeRange.Days7, L10n.Get("Insights.Range7Days")));
        Ranges.Add(new InsightsRangeOption(InsightsTimeRange.Days30, L10n.Get("Insights.Range30Days")));
        Ranges.Add(new InsightsRangeOption(InsightsTimeRange.Days90, L10n.Get("Insights.Range90Days")));
        Ranges.Add(new InsightsRangeOption(InsightsTimeRange.All, L10n.Get("Insights.RangeAll")));
        SelectedRange = Ranges[1];

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => !IsExporting && HasData && SelectedAccount is not null);
    }

    partial void OnIsExportingChanged(bool value) => ExportCsvCommand.NotifyCanExecuteChanged();

    partial void OnHasDataChanged(bool value) => ExportCsvCommand.NotifyCanExecuteChanged();

    partial void OnSelectedAccountChanged(InsightsAccountOption? value)
    {
        if (value is not null)
        {
            _ = LoadForAccountAsync(value.AccountId);
        }
    }

    partial void OnSelectedMetricChanged(InsightsMetricOption? value) =>
        _ = RebuildAsync();

    partial void OnSelectedRangeChanged(InsightsRangeOption value) =>
        _ = RebuildAsync();

    /// <summary>加载账户列表（数据洞察页进入时调用；不重复加载全部历史）。</summary>
    public async Task LoadAccountsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var accounts = await _accountManager.GetAllAccountsAsync(cancellationToken);
            string? preferredId = SelectedAccount?.AccountId;
            Accounts.Clear();
            foreach (var account in accounts.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                Accounts.Add(new InsightsAccountOption(account.AccountId, account.DisplayName));
            }

            if (Accounts.Count == 0)
            {
                HasData = false;
                EmptyMessage = L10n.Get("Insights.NoAccount");
                return;
            }

            // 保留之前选择的账户（若仍存在）；否则不自动选择（等待“查看趋势”入口或用户选择）。
            if (preferredId is { } preferred
                && Accounts.FirstOrDefault(a => string.Equals(a.AccountId, preferred, StringComparison.OrdinalIgnoreCase))
                    is { } existing)
            {
                SelectedAccount = existing;
            }
            else if (SelectedAccount is null && TargetAccountId is null)
            {
                // 首次进入且无目标账户：自动选第一个，便于展示。
                SelectedAccount = Accounts[0];
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"加载洞察账户失败: {ex.GetType().Name}");
        }
    }

    /// <summary>按账户 ID 预选（账户卡片“查看趋势”入口）。</summary>
    public void SelectAccount(string accountId)
    {
        var option = Accounts.FirstOrDefault(a =>
            string.Equals(a.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            SelectedAccount = option;
        }
    }

    /// <summary>刷新当前选择（账户卡片刷新后调用）。</summary>
    public async Task RefreshAsync()
    {
        if (SelectedAccount is { } account)
        {
            await LoadForAccountAsync(account.AccountId);
        }
    }

    /// <summary>页面离开时释放大型集合并取消在途请求。</summary>
    public void Release()
    {
        _queryCts?.Cancel();
        _queryCts?.Dispose();
        _queryCts = null;
        TrendPoints.Clear();
        HistoryRows.Clear();
        _loadedHistory = Array.Empty<BalanceHistoryEntry>();
        HasData = false;
    }

    public void Shutdown() => _lifetime.Cancel();

    private async Task LoadForAccountAsync(string accountId)
    {
        _queryCts?.Cancel();
        _queryCts?.Dispose();
        _queryCts = new CancellationTokenSource();
        var ct = _queryCts.Token;

        IsLoading = true;
        HasData = false;
        HasStatus = false;
        TrendPoints.Clear();
        HistoryRows.Clear();
        Metrics.Clear();
        try
        {
            var history = await _historyProvider.GetHistoryAsync(accountId, ct);
            _loadedHistory = history;
            UpdateSuccessRate(accountId, history);

            // 指标列表来自该账户历史中的通用 BalanceMetric（不得写死）。
            var metricSet = new Dictionary<string, InsightsMetricOption>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in history)
            {
                foreach (var metric in entry.Metrics)
                {
                    if (!metricSet.ContainsKey(metric.MetricId))
                    {
                        metricSet[metric.MetricId] = new InsightsMetricOption(
                            metric.MetricId,
                            metric.DisplayName,
                            metric.Unit,
                            metric.Kind,
                            metric.ValueKind,
                            metric.DetailedKind);
                    }
                }
            }

            foreach (var metric in metricSet.Values.OrderBy(m => m.MetricId, StringComparer.OrdinalIgnoreCase))
            {
                Metrics.Add(metric);
            }

            if (Metrics.Count == 0)
            {
                HasData = false;
                EmptyMessage = L10n.Get("Insights.EmptyState");
                return;
            }

            // 保留之前选择的指标（若仍存在）；否则选第一个。
            string? preferred = SelectedMetric?.MetricId;
            SelectedMetric = metricSet.Values.FirstOrDefault(m =>
                string.Equals(m.MetricId, preferred, StringComparison.OrdinalIgnoreCase))
                ?? metricSet.Values.First();

            // 填充历史表格（当前范围全部行；大数据由 UI 虚拟化）。
            FillHistoryRows(ct);

            await RebuildAsync(ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"加载洞察数据失败: {ex.GetType().Name}");
            HasData = false;
            EmptyMessage = L10n.Get("Insights.LoadError");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private async Task RebuildAsync(CancellationToken? external = null)
    {
        if (SelectedAccount is null || SelectedMetric is null || _loadedHistory.Count == 0)
        {
            return;
        }

        var ct = external ?? _queryCts?.Token ?? CancellationToken.None;
        if (ct.IsCancellationRequested)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var points = _trendBuilder.Build(
                _loadedHistory,
                SelectedMetric.MetricId,
                SelectedRange.Range,
                DateTimeOffset.UtcNow);

            TrendPoints.Clear();
            foreach (var point in points)
            {
                TrendPoints.Add(point);
            }

            UpdateSummaryValues(points, ct);
            FillHistoryRows(ct);
            HasData = points.Count > 0 || HistoryRows.Count > 0;
            EmptyMessage = HasData ? string.Empty : L10n.Get("Insights.EmptyState");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private void UpdateSummaryValues(IReadOnlyList<TrendPoint> points, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var metric = SelectedMetric!;
        bool numericKind = metric.ValueKind is MetricValueKind.Decimal or MetricValueKind.Integer;
        var values = numericKind
            ? points.Where(p => p.Value is not null).Select(p => p.Value!.Value).ToList()
            : new List<decimal>();

        if (values.Count == 0)
        {
            CurrentValueText = L10n.Get("Insights.UnknownValue");
            ChangeInRangeText = "—";
            FirstValueText = "—";
            LatestValueText = "—";
            MinimumValueText = "—";
            MaximumValueText = "—";
            DailyConsumptionText = "—";
            EstimatedDaysLeftText = "—";
            EstimateExplanationText = string.Empty;
            ChartSummaryText = L10n.Get("Insights.NoData");
            HasChartSummary = false;

            // v0.9.0：状态/布尔/时间戳指标显示最新状态文本，不伪造数值趋势。
            if (metric.ValueKind != MetricValueKind.Decimal)
            {
                CurrentValueText = LatestStatusText();
            }

            return;
        }

        decimal current = values[^1];
        decimal first = values[0];
        decimal min = values.Min();
        decimal max = values.Max();

        CurrentValueText = FormatValue(current, metric.Unit);
        FirstValueText = FormatValue(first, metric.Unit);
        LatestValueText = FormatValue(current, metric.Unit);
        MinimumValueText = FormatValue(min, metric.Unit);
        MaximumValueText = FormatValue(max, metric.Unit);
        ChangeInRangeText = FormatChange(current - first, metric.Unit);

        // 估算（仅剩余/额度类数值指标；状态、计数、延迟不估算消耗速度/天数）。
        ConsumptionEstimate estimate;
        if (metric.ValueKind is not (MetricValueKind.Decimal or MetricValueKind.Integer)
            || metric.DetailedKind is MetricKind.CredentialStatus
                or MetricKind.PermissionStatus or MetricKind.QuotaState
                or MetricKind.ServiceAvailability)
        {
            estimate = new ConsumptionEstimate
            {
                IsAvailable = false,
                UnavailableReason = EstimateUnavailableReason.UnsupportedMetric,
            };
        }
        else
        {
            estimate = _estimateService.Estimate(
                points.Select(p => new TimePoint(p.TimeUtc, p.Value)).ToList(),
                metric.Kind,
                current,
                isUnlimited: false);
        }

        if (estimate.IsAvailable && estimate.DailyConsumption > 0)
        {
            DailyConsumptionText = L10n.Format("Insights.PerDayFormat", FormatValue(estimate.DailyConsumption, metric.Unit));
            EstimatedDaysLeftText = estimate.EstimatedDaysLeft is { } days
                ? days.ToString("0.#", CultureInfo.CurrentCulture)
                : "—";
            EstimateExplanationText = L10n.Get("Insights.EstimateExplanation");
        }
        else
        {
            DailyConsumptionText = "—";
            EstimatedDaysLeftText = "—";
            EstimateExplanationText = L10n.Format("Insights.EstimateUnavailableFormat", UnavailableReasonText(estimate.UnavailableReason));
        }

        string timeSpan = $"{points[0].TimeUtc.ToLocalTime():MM-dd} ~ {points[^1].TimeUtc.ToLocalTime():MM-dd}";
        ChartSummaryText =
            L10n.Format("Insights.ChartSummaryFormat", metric.DisplayName, points.Count, timeSpan, MinimumValueText, MaximumValueText);
        HasChartSummary = true;
    }

    private void FillHistoryRows(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HistoryRows.Clear();
        if (SelectedMetric is null)
        {
            return;
        }

        var fromUtc = SelectedRange.Range switch
        {
            InsightsTimeRange.Days7 => DateTimeOffset.UtcNow.AddDays(-7),
            InsightsTimeRange.Days30 => DateTimeOffset.UtcNow.AddDays(-30),
            InsightsTimeRange.Days90 => DateTimeOffset.UtcNow.AddDays(-90),
            _ => (DateTimeOffset?)null,
        };

        foreach (var entry in _loadedHistory
            .Where(e => fromUtc is null || e.SucceededAtUtc >= fromUtc)
            .OrderByDescending(e => e.SucceededAtUtc))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var metric in entry.Metrics)
            {
                if (!string.Equals(metric.MetricId, SelectedMetric.MetricId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                HistoryRows.Add(new InsightsHistoryRow
                {
                    TimeText = entry.SucceededAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
                    ProviderId = entry.ProviderId,
                    AccountDisplayName = SelectedAccount?.DisplayName ?? string.Empty,
                    MetricDisplayName = metric.DisplayName,
                    ValueText = MetricValueProvider.ValueText(metric),
                    Unit = metric.Unit,
                    SourceText = entry.Source == BalanceQuerySource.Automatic ? L10n.Get("Insights.SourceAutomatic") : L10n.Get("Insights.SourceManual"),
                });
                break;
            }
        }
    }

    private string LatestStatusText()
    {
        var metric = SelectedMetric;
        if (metric is null)
        {
            return L10n.Get("Insights.UnknownValue");
        }

        var latest = _loadedHistory
            .Where(h => h.Metrics.Any(m =>
                string.Equals(m.MetricId, metric.MetricId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(h => h.SucceededAtUtc)
            .Select(h => h.Metrics.First(m =>
                string.Equals(m.MetricId, metric.MetricId, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault();
        return latest is null
            ? L10n.Get("Insights.UnknownValue")
            : MetricValueProvider.ValueText(latest);
    }

    /// <summary>服务成功率 = 健康探测数 / 探测总数（来自 service.availability 历史）。</summary>
    private void UpdateSuccessRate(string accountId, IReadOnlyList<BalanceHistoryEntry> history)
    {
        var entries = history
            .Where(h => h.Metrics.Any(m =>
                m.DetailedKind == MetricKind.ServiceAvailability))
            .ToList();
        if (entries.Count == 0)
        {
            SuccessRateText = string.Empty;
            HasSuccessRate = false;
            return;
        }

        int healthy = entries.Count(h => h.Metrics.Any(m =>
            m.DetailedKind == MetricKind.ServiceAvailability
            && m.StatusValue is { } status
            && string.Equals(status, nameof(GeospatialStatus.Healthy), StringComparison.OrdinalIgnoreCase)));
        int percent = (int)Math.Round(healthy * 100.0 / entries.Count);
        SuccessRateText = L10n.Format("Insights.SuccessRateFormat", percent, healthy, entries.Count);
        HasSuccessRate = true;
    }

    private async Task ExportCsvAsync()
    {
        if (SelectedAccount is null || !HasData)
        {
            return;
        }

        string? path = await _filePicker.PickSaveFileAsync(
            $"ApiMonitor-history-{SelectedAccount.AccountId}-{DateTimeOffset.Now:yyyyMMdd}.csv",
            new[] { ".csv" },
            _lifetime.Token);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        IsExporting = true;
        HasStatus = false;
        try
        {
            var accounts = await _accountManager.GetAllAccountsAsync(_lifetime.Token);
            var byId = accounts.ToDictionary(a => a.AccountId, StringComparer.OrdinalIgnoreCase);
            string csv = await _csvExporter.ExportAsync(_loadedHistory, byId, _lifetime.Token);
            // UTF-8 with BOM。
            await File.WriteAllTextAsync(path, csv, new System.Text.UTF8Encoding(true), _lifetime.Token);
            StatusMessage = L10n.Get("Insights.ExportCsvDone");
            HasStatus = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"导出 CSV 失败: {ex.GetType().Name}");
            StatusMessage = L10n.Get("Insights.ExportCsvFailed");
            HasStatus = true;
        }
        finally
        {
            IsExporting = false;
        }
    }

    private static string FormatValue(decimal value, string unit) =>
        string.IsNullOrWhiteSpace(unit)
            ? value.ToString("0.##", CultureInfo.CurrentCulture)
            : $"{value.ToString("0.##", CultureInfo.CurrentCulture)} {unit}";

    private static string FormatChange(decimal change, string unit) =>
        change == 0
            ? "0"
            : (change > 0 ? "+" : "−") + FormatValue(Math.Abs(change), unit);

    private static string UnavailableReasonText(EstimateUnavailableReason? reason) =>
        reason switch
        {
            EstimateUnavailableReason.NotEnoughData => L10n.Get("Insights.NotEnoughData"),
            EstimateUnavailableReason.TimeSpanTooShort => L10n.Get("Insights.TimeSpanTooShort"),
            EstimateUnavailableReason.NoConsumption => L10n.Get("Insights.NoConsumption"),
            EstimateUnavailableReason.RecentTopUpOrReset => L10n.Get("Insights.RecentTopUp"),
            EstimateUnavailableReason.UnsupportedMetric => L10n.Get("Insights.UnsupportedMetric"),
            EstimateUnavailableReason.UnknownCurrentValue => L10n.Get("Insights.UnknownCurrentValue"),
            EstimateUnavailableReason.NonPositiveConsumption => L10n.Get("Insights.NonPositiveConsumption"),
            _ => L10n.Get("Insights.CannotEstimate"),
        };
}
