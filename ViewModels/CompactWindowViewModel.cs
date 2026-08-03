using System.Collections.ObjectModel;
using ApiMonitor.Helpers;
using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>紧凑窗口账户选择项（使用账户 ID 作为持久化主键）。</summary>
public sealed record CompactAccountOption(string AccountId, string DisplayName, string ProviderDisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>紧凑窗口指标选择项（使用 MetricId 作为稳定标识）。</summary>
public sealed record CompactMetricOption(string MetricId, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 紧凑余额窗口 ViewModel：复用 IAccountManager 的查询与并发保护，
/// 不创建第二套 HttpClient，不单独保存另一份余额。
/// 所有 UI 更新必须通过 IUiThreadInvoker 回到窗口所属 DispatcherQueue。
/// </summary>
public sealed partial class CompactWindowViewModel : ObservableObject
{
    private readonly IAccountManager _accountManager;
    private readonly ICompactWindowSettingsStore _settingsStore;
    private readonly AppLog _log;
    private readonly IUiThreadInvoker _ui;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isInitialized;
    private bool _isShutdown;
    private bool _suppressSelectionRebuild;
    private AccountBalanceRecord? _currentRecord;
    private ApiAccount? _currentAccount;

    public ObservableCollection<CompactAccountOption> AccountOptions { get; } = new();

    public ObservableCollection<CompactMetricOption> MetricOptions { get; } = new();

    [ObservableProperty]
    private CompactAccountOption? _selectedAccount;

    [ObservableProperty]
    private CompactMetricOption? _selectedMetric;

    [ObservableProperty]
    private bool _isAlwaysOnTop = true;

    [ObservableProperty]
    private bool _hasAccounts;

    [ObservableProperty]
    private bool _hasSnapshot;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _balanceText = "—";

    [ObservableProperty]
    private string _statusText = "尚未查询余额";

    [ObservableProperty]
    private string _lastSuccessText = "尚未成功更新";

    [ObservableProperty]
    private string _refreshStatusText = "自动刷新已开启";

    [ObservableProperty]
    private string _nextRefreshText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>置顶开关变化时通知窗口同步 OverlappedPresenter。</summary>
    public event EventHandler? AlwaysOnTopChanged;

    /// <summary>用户请求打开主窗口。</summary>
    public event EventHandler? OpenMainWindowRequested;

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand OpenMainWindowCommand { get; }

    public CompactWindowViewModel(
        IAccountManager accountManager,
        ICompactWindowSettingsStore settingsStore,
        AppLog log,
        IUiThreadInvoker ui)
    {
        _accountManager = accountManager;
        _settingsStore = settingsStore;
        _log = log;
        _ui = ui;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        OpenMainWindowCommand = new RelayCommand(
            () => OpenMainWindowRequested?.Invoke(this, EventArgs.Empty));

        _accountManager.RefreshStarted += OnRefreshStarted;
        _accountManager.RefreshCompleted += OnRefreshCompleted;
        _accountManager.AccountsChanged += OnAccountsChanged;
    }

    /// <summary>窗口显示前初始化：读取设置并按保存的选择恢复账户/币种。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        IsAlwaysOnTop = settings.IsAlwaysOnTop;
        // v0.4.0 遗留币种选择 → 迁移为 DeepSeek 货币总余额指标 ID。
        string? preferredMetricId = settings.SelectedMetricId;
        if (string.IsNullOrEmpty(preferredMetricId) && !string.IsNullOrEmpty(settings.SelectedCurrency))
        {
            preferredMetricId = $"deepseek:{settings.SelectedCurrency}:total";
        }

        await ReloadAccountsCoreAsync(settings.SelectedAccountId, preferredMetricId, cancellationToken);
    }

    partial void OnIsAlwaysOnTopChanged(bool value)
    {
        AlwaysOnTopChanged?.Invoke(this, EventArgs.Empty);
        _ = PersistSettingsAsync();
    }

    partial void OnSelectedAccountChanged(CompactAccountOption? value)
    {
        if (!_suppressSelectionRebuild)
        {
            _ = HandleSelectedAccountChangedAsync(value);
        }
        else
        {
            _ = PersistSettingsAsync();
        }
    }

    partial void OnSelectedMetricChanged(CompactMetricOption? value)
    {
        UpdateDisplay();
        _ = PersistSettingsAsync();
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();

    /// <summary>手动刷新：复用账户级并发锁，防止与自动刷新重复执行。</summary>
    public async Task RefreshAsync()
    {
        if (IsBusy || SelectedAccount is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _accountManager.RefreshAccountAsync(
                SelectedAccount.AccountId,
                BalanceQuerySource.Manual,
                _lifetime.Token);

            if (result.Error?.Kind == BalanceErrorKind.Busy)
            {
                ErrorText = "该账户正在查询，请稍候。";
                HasError = true;
                return;
            }

            await ApplyResultAsync(SelectedAccount.AccountId, result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"紧凑窗口刷新失败: {ex.GetType().Name}");
            ErrorText = "刷新失败，请稍后重试。";
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>取消订阅并取消在途操作（窗口关闭/应用退出时调用）。</summary>
    public void Shutdown()
    {
        if (_isShutdown)
        {
            return;
        }

        _isShutdown = true;
        _accountManager.RefreshStarted -= OnRefreshStarted;
        _accountManager.RefreshCompleted -= OnRefreshCompleted;
        _accountManager.AccountsChanged -= OnAccountsChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task ReloadAccountsCoreAsync(
        string? preferredAccountId,
        string? preferredMetricId,
        CancellationToken cancellationToken)
    {
        var accounts = await _accountManager.GetAllAccountsAsync(cancellationToken);
        var ordered = accounts
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var providerNames = _accountManager.Providers
            .ToDictionary(p => p.ProviderId, p => p.DisplayName);

        AccountOptions.Clear();
        foreach (var account in ordered)
        {
            string providerName = providerNames.TryGetValue(account.ProviderId, out var name)
                ? name
                : account.ProviderId;
            AccountOptions.Add(new CompactAccountOption(
                account.AccountId,
                account.DisplayName,
                providerName));
        }

        HasAccounts = AccountOptions.Count > 0;
        if (!HasAccounts)
        {
            SelectedAccount = null;
            SelectedMetric = null;
            MetricOptions.Clear();
            HasSnapshot = false;
            BalanceText = "—";
            StatusText = "尚未添加 API 账户";
            LastSuccessText = "尚未成功更新";
            ErrorText = string.Empty;
            HasError = false;
            RefreshStatusText = "自动刷新已开启";
            NextRefreshText = string.Empty;
            return;
        }

        // 上次账户已删除时自动选择第一个可用账户；否则保留原选择。
        // 程序化重建期间抑制选择变化触发的二次重建，保证确定性。
        _suppressSelectionRebuild = true;
        try
        {
            var target = AccountOptions.FirstOrDefault(o => o.AccountId == preferredAccountId)
                ?? AccountOptions[0];
            SelectedAccount = target;
            await RebuildMetricOptionsAsync(target, preferredMetricId, cancellationToken);
        }
        finally
        {
            _suppressSelectionRebuild = false;
        }
    }

    private async Task RebuildMetricOptionsAsync(
        CompactAccountOption account,
        string? preferredMetricId,
        CancellationToken cancellationToken)
    {
        _currentRecord = await _accountManager.GetRecordAsync(account.AccountId, cancellationToken);
        _currentAccount = await _accountManager.GetAccountAsync(account.AccountId, cancellationToken);
        var metrics = _currentRecord?.LastSuccessfulSnapshot?.Metrics
            .Where(m => !string.IsNullOrWhiteSpace(m.MetricId))
            .DistinctBy(m => m.MetricId, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<BalanceMetric>();

        MetricOptions.Clear();
        foreach (var metric in metrics)
        {
            MetricOptions.Add(new CompactMetricOption(metric.MetricId, metric.DisplayName));
        }

        if (metrics.Count == 0)
        {
            SelectedMetric = null;
            HasSnapshot = false;
            UpdateDisplayFromRecord(_currentRecord);
            return;
        }

        // 上次指标已消失时自动选择当前快照的第一个指标。
        var targetMetric = metrics.FirstOrDefault(m =>
            string.Equals(m.MetricId, preferredMetricId, StringComparison.OrdinalIgnoreCase))
            ?? metrics[0];
        SelectedMetric = new CompactMetricOption(targetMetric.MetricId, targetMetric.DisplayName);
        HasSnapshot = true;
        UpdateDisplayFromRecord(_currentRecord);
    }

    private void UpdateDisplayFromRecord(AccountBalanceRecord? record)
    {
        var snapshot = record?.LastSuccessfulSnapshot;
        if (snapshot is null)
        {
            HasSnapshot = false;
            BalanceText = "—";
            StatusText = "尚未查询余额";
            LastSuccessText = record?.LastQuerySuccessAt is { } last
                ? FormatTime(last)
                : "尚未成功更新";
            ErrorText = record?.LastQueryAttemptAt is not null ? "最近一次查询未成功" : string.Empty;
            HasError = !string.IsNullOrEmpty(ErrorText);
            UpdateRefreshStatus(record);
            return;
        }

        HasSnapshot = true;
        LastSuccessText = FormatTime(snapshot.RetrievedAt);
        UpdateDisplay();
        UpdateRefreshStatus(record);
    }

    /// <summary>按当前账户/币种重算余额与状态（阈值变化后也会立即反映）。</summary>
    private void UpdateDisplay()
    {
        if (SelectedAccount is null)
        {
            HasSnapshot = false;
            BalanceText = "—";
            StatusText = "尚未添加 API 账户";
            return;
        }

        var snapshot = _currentRecord?.LastSuccessfulSnapshot;
        if (snapshot is null || snapshot.Metrics.Count == 0)
        {
            HasSnapshot = false;
            BalanceText = "—";
            StatusText = "尚未查询余额";
            ErrorText = _currentRecord?.LastQueryAttemptAt is not null ? "最近一次查询未成功" : string.Empty;
            HasError = !string.IsNullOrEmpty(ErrorText);
            return;
        }

        var metric = snapshot.Metrics.FirstOrDefault(m =>
            string.Equals(m.MetricId, SelectedMetric?.MetricId, StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Metrics[0];

        if (SelectedMetric is null || !snapshot.Metrics.Any(m =>
            string.Equals(m.MetricId, SelectedMetric.MetricId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedMetric = new CompactMetricOption(metric.MetricId, metric.DisplayName);
            return;
        }

        HasSnapshot = true;
        BalanceText = BalanceMetricText.FormatAmount(BalanceMetricText.MainAmount(metric));
        LastSuccessText = FormatTime(snapshot.RetrievedAt);

        var rule = _currentAccount?.Monitoring.Thresholds.FirstOrDefault(r =>
            string.Equals(r.MetricId, metric.MetricId, StringComparison.OrdinalIgnoreCase));
        StatusText = ThresholdEvaluator.Evaluate(metric, rule) switch
        {
            ThresholdStatus.BelowThreshold => "低余额",
            ThresholdStatus.Normal => "正常",
            _ => "未知",
        };

    }

    private void UpdateRefreshStatus(AccountBalanceRecord? record)
    {
        if (SelectedAccount is null)
        {
            return;
        }

        if (_currentAccount is null)
        {
            return;
        }

        bool autoEnabled = _currentAccount.Monitoring.AutoRefreshEnabled;
        RefreshStatusText = autoEnabled ? "自动刷新已开启" : "自动刷新已关闭";
        NextRefreshText = autoEnabled && _currentAccount.Monitoring.NextRefreshAtUtc is { } next
            ? "下次刷新：" + FormatTime(next)
            : string.Empty;
    }

    private async Task HandleSelectedAccountChangedAsync(CompactAccountOption? account)
    {
        await PersistSettingsAsync();
        if (account is null)
        {
            MetricOptions.Clear();
            HasSnapshot = false;
            BalanceText = "—";
            StatusText = "尚未添加 API 账户";
            return;
        }

        await RebuildMetricOptionsAsync(account, SelectedMetric?.MetricId, _lifetime.Token);
    }

    private async Task ApplyResultAsync(string accountId, BalanceQueryResult result)
    {
        if (SelectedAccount is null
            || !string.Equals(SelectedAccount.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentRecord = await _accountManager.GetRecordAsync(accountId, _lifetime.Token);
        _currentAccount = await _accountManager.GetAccountAsync(accountId, _lifetime.Token);
        if (result.IsSuccess && result.Snapshot is { } snapshot)
        {
            // 新币种出现后列表立即更新。
            ErrorText = string.Empty;
            HasError = false;
            await RebuildMetricOptionsAsync(SelectedAccount, SelectedMetric?.MetricId, _lifetime.Token);
            return;
        }

        ErrorText = result.Error?.Message ?? "查询失败。";
        HasError = true;
        UpdateRefreshStatus(_currentRecord);
    }

    private void OnRefreshStarted(object? sender, AccountRefreshStartedEventArgs e)
    {
        _ui.Post(() =>
        {
            if (SelectedAccount is not null
                && string.Equals(SelectedAccount.AccountId, e.AccountId, StringComparison.OrdinalIgnoreCase))
            {
                IsBusy = true;
                ErrorText = string.Empty;
                HasError = false;
            }
        });
    }

    private void OnRefreshCompleted(object? sender, AccountRefreshCompletedEventArgs e)
    {
        _ui.Post(() => _ = HandleRefreshCompletedAsync(e));
    }

    private async Task HandleRefreshCompletedAsync(AccountRefreshCompletedEventArgs e)
    {
        if (SelectedAccount is null
            || !string.Equals(SelectedAccount.AccountId, e.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IsBusy = false;
        await ApplyResultAsync(e.AccountId, e.Result);
    }

    private void OnAccountsChanged(object? sender, EventArgs e)
    {
        _ui.Post(() => _ = ReloadOnAccountsChangedAsync());
    }

    private async Task ReloadOnAccountsChangedAsync()
    {
        if (_isShutdown)
        {
            return;
        }

        var settings = await _settingsStore.LoadAsync(_lifetime.Token);
        await ReloadAccountsCoreAsync(settings.SelectedAccountId, settings.SelectedMetricId, _lifetime.Token);
    }

    private async Task PersistSettingsAsync()
    {
        if (_isShutdown)
        {
            return;
        }

        try
        {
            var settings = await _settingsStore.LoadAsync(_lifetime.Token);
            settings.IsAlwaysOnTop = IsAlwaysOnTop;
            settings.SelectedAccountId = SelectedAccount?.AccountId;
            settings.SelectedMetricId = SelectedMetric?.MetricId;
            settings.SelectedCurrency = null;
            await _settingsStore.SaveAsync(settings, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"保存紧凑窗口设置失败: {ex.GetType().Name}");
        }
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
