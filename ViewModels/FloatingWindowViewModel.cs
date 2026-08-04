using ApiMonitor.Helpers;
using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiMonitor.ViewModels;

/// <summary>
/// 悬浮余额窗 ViewModel（v0.7.0）：只显示一个选定账户的核心额度数字，
/// 不再包含账户/指标切换表单、刷新按钮或置顶开关。
/// 主额度选择规则集中在 <see cref="MainBalanceMetricSelector"/>。
/// 所有 UI 更新必须通过 IUiThreadInvoker 回到窗口所属 DispatcherQueue。
/// </summary>
public sealed partial class FloatingWindowViewModel : ObservableObject
{
    private readonly IAccountManager _accountManager;
    private readonly IFloatingWindowSettingsStore _settingsStore;
    private readonly AppLog _log;
    private readonly IUiThreadInvoker _ui;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isInitialized;
    private bool _isShutdown;

    [ObservableProperty]
    private bool _hasAccount;

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private string _providerName = string.Empty;

    [ObservableProperty]
    private string _balanceText = "—";

    [ObservableProperty]
    private string _unitText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _lastUpdatedText = string.Empty;

    [ObservableProperty]
    private string _emptyText = string.Empty;

    /// <summary>置顶固定为开启（v0.7.0 不提供 UI 配置）。</summary>
    public bool IsAlwaysOnTop => true;

    public FloatingWindowViewModel(
        IAccountManager accountManager,
        IFloatingWindowSettingsStore settingsStore,
        AppLog log,
        IUiThreadInvoker ui)
    {
        _accountManager = accountManager;
        _settingsStore = settingsStore;
        _log = log;
        _ui = ui;

        _accountManager.RefreshStarted += OnRefreshStarted;
        _accountManager.RefreshCompleted += OnRefreshCompleted;
        _accountManager.AccountsChanged += OnAccountsChanged;
    }

    /// <summary>窗口显示前初始化：读取设置并恢复上次选中的账户。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        await ReloadAsync(settings.SelectedAccountId, cancellationToken);
    }

    /// <summary>把指定账户设为悬浮窗账户并立即刷新显示（未打开则先打开）。</summary>
    public async Task ShowAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        if (_isShutdown || string.IsNullOrWhiteSpace(accountId))
        {
            return;
        }

        await PersistSelectedAccountAsync(accountId, cancellationToken);
        await ReloadAsync(accountId, cancellationToken);
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

    private async Task ReloadAsync(string? preferredAccountId, CancellationToken cancellationToken)
    {
        var accounts = await _accountManager.GetAllAccountsAsync(cancellationToken);
        var providerNames = _accountManager.Providers
            .ToDictionary(p => p.ProviderId, p => p.DisplayName, StringComparer.OrdinalIgnoreCase);

        ApiAccount? selected = accounts.FirstOrDefault(a =>
            string.Equals(a.AccountId, preferredAccountId, StringComparison.OrdinalIgnoreCase));

        if (selected is null)
        {
            // 当前选定账户被删除或从未选择：安全回退为空状态，不崩溃。
            HasAccount = false;
            AccountName = string.Empty;
            ProviderName = string.Empty;
            BalanceText = "—";
            UnitText = string.Empty;
            StatusText = string.Empty;
            LastUpdatedText = string.Empty;
            EmptyText = accounts.Count == 0
                ? L10n.Get("Floating.NoAccounts")
                : L10n.Get("Floating.NoSelectedAccount");
            return;
        }

        var record = await _accountManager.GetRecordAsync(selected.AccountId, cancellationToken);
        HasAccount = true;
        AccountName = selected.DisplayName;
        ProviderName = providerNames.TryGetValue(selected.ProviderId, out var providerName)
            ? providerName
            : selected.ProviderId;
        EmptyText = string.Empty;
        UpdateDisplay(selected, record);
    }

    private void UpdateDisplay(ApiAccount account, AccountBalanceRecord? record)
    {
        var snapshot = record?.LastSuccessfulSnapshot;

        // 查询失败：显示失败状态，而不是错误的旧数字。
        if (record?.LastQueryAttemptAt is { } attempt
            && (record.LastSuccessfulSnapshot is null
                || record.LastQuerySuccessAt is not { } success
                || attempt > success))
        {
            BalanceText = "—";
            UnitText = string.Empty;
            StatusText = L10n.Get("Floating.QueryFailed");
            LastUpdatedText = L10n.Get("Card.NotUpdatedYet");
            return;
        }

        if (snapshot is null || snapshot.Metrics.Count == 0)
        {
            BalanceText = "—";
            UnitText = string.Empty;
            StatusText = L10n.Get("Floating.NotQueried");
            LastUpdatedText = L10n.Get("Card.NotUpdatedYet");
            return;
        }

        var metric = MainBalanceMetricSelector.Select(snapshot.Metrics);
        if (metric is null)
        {
            // 无可显示主额度：显示“未知”，绝不用 0 或旧值。
            BalanceText = L10n.Get("Floating.Unknown");
            UnitText = string.Empty;
            StatusText = L10n.Get("Home.StatusUnknown");
            LastUpdatedText = FormatTime(snapshot.RetrievedAt);
            return;
        }

        decimal? amount = MainBalanceMetricSelector.MainAmount(metric);
        if (amount is null)
        {
            // 无可显示主额度：显示“未知”，绝不用 0 或旧值。
            BalanceText = L10n.Get("Floating.Unknown");
            UnitText = metric.Unit;
            StatusText = L10n.Get("Home.StatusUnknown");
            LastUpdatedText = FormatTime(snapshot.RetrievedAt);
            return;
        }

        BalanceText = BalanceFormatter.Format(amount.Value);
        UnitText = metric.Unit;

        var rule = account.Monitoring.Thresholds.FirstOrDefault(r =>
            string.Equals(r.MetricId, metric.MetricId, StringComparison.OrdinalIgnoreCase));
        StatusText = ThresholdEvaluator.Evaluate(metric, rule) switch
        {
            ThresholdStatus.BelowThreshold => L10n.Get("Home.StatusLow"),
            ThresholdStatus.Normal => L10n.Get("Home.StatusNormal"),
            _ => L10n.Get("Home.StatusUnknown"),
        };
        LastUpdatedText = L10n.Format("Floating.LastUpdatedFormat", FormatTime(snapshot.RetrievedAt));
    }

    private async Task PersistSelectedAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        if (_isShutdown)
        {
            return;
        }

        try
        {
            var settings = await _settingsStore.LoadAsync(cancellationToken);
            settings.SelectedAccountId = accountId;
            await _settingsStore.SaveAsync(settings, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"保存悬浮窗设置失败: {ex.GetType().Name}");
        }
    }

    private void OnRefreshStarted(object? sender, AccountRefreshStartedEventArgs e)
    {
        _ui.Post(() =>
        {
            if (!HasAccount)
            {
                return;
            }

            // 刷新进行中保持当前显示，完成后由 RefreshCompleted 统一更新。
        });
    }

    private void OnRefreshCompleted(object? sender, AccountRefreshCompletedEventArgs e)
    {
        _ui.Post(() => _ = HandleRefreshCompletedAsync(e));
    }

    private void OnAccountsChanged(object? sender, EventArgs e)
    {
        _ui.Post(() => _ = ReloadOnAccountsChangedAsync());
    }

    private async Task HandleRefreshCompletedAsync(AccountRefreshCompletedEventArgs e)
    {
        if (_isShutdown)
        {
            return;
        }

        // 只刷新当前选中账户的显示（其他账户完成不影响本窗）。
        var settings = await _settingsStore.LoadAsync(_lifetime.Token);
        if (!string.Equals(settings.SelectedAccountId, e.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await ReloadAsync(settings.SelectedAccountId, _lifetime.Token);
    }

    private async Task ReloadOnAccountsChangedAsync()
    {
        if (_isShutdown)
        {
            return;
        }

        var settings = await _settingsStore.LoadAsync(_lifetime.Token);
        await ReloadAsync(settings.SelectedAccountId, _lifetime.Token);
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
