using System.Collections.ObjectModel;
using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>主界面 Provider 筛选选项（ProviderId 为空字符串表示“全部”）。</summary>
public sealed record ProviderFilterOption(string ProviderId, string DisplayName);

/// <summary>主界面状态筛选选项。</summary>
public sealed record StatusFilterOption(AccountStatusFilter Filter, string DisplayName);

/// <summary>
/// 主界面 ViewModel。所有网络与持久化操作都通过服务接口完成；
/// 自动刷新结果通过事件 + UI 线程调用器回写账户卡片。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAccountManager _accountManager;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly IUiThreadInvoker _ui;
    private readonly AppLog _log;
    private readonly Action _openCompactWindow;
    private readonly Func<string, CancellationToken, Task<DateTimeOffset?>>? _snoozeReader;
    private readonly CancellationTokenSource _lifetime = new();
    private int _statusGeneration;

    /// <summary>当前导航页面（默认主页；通知激活会强制回到主页）。</summary>
    [ObservableProperty]
    private AppPageKind _currentPage = AppPageKind.Home;

    public ObservableCollection<AccountListItemViewModel> Accounts { get; } = new();

    /// <summary>经过 Provider/状态筛选后实际显示的账户列表（ListView 绑定此集合）。</summary>
    public ObservableCollection<AccountListItemViewModel> FilteredAccounts { get; } = new();

    /// <summary>Provider 筛选选项：全部 + 注册表中的每个 Provider。</summary>
    public IReadOnlyList<ProviderFilterOption> ProviderFilterOptions { get; private set; } =
        new[] { new ProviderFilterOption(string.Empty, L10n.Get("Home.FilterAllProviders")) };

    public IReadOnlyList<StatusFilterOption> StatusFilterOptions { get; } = new[]
    {
        new StatusFilterOption(AccountStatusFilter.All, L10n.Get("Home.FilterAllStatus")),
        new StatusFilterOption(AccountStatusFilter.Normal, L10n.Get("Home.StatusNormal")),
        new StatusFilterOption(AccountStatusFilter.Low, L10n.Get("Home.StatusLow")),
        new StatusFilterOption(AccountStatusFilter.Unknown, L10n.Get("Home.StatusUnknown")),
        new StatusFilterOption(AccountStatusFilter.Failed, L10n.Get("Home.StatusFailed")),
    };

    [ObservableProperty]
    private string _selectedProviderFilter = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private AccountStatusFilter _selectedStatusFilter = AccountStatusFilter.All;

    [ObservableProperty]
    private int _totalAccountCount;

    [ObservableProperty]
    private int _lowBalanceAccountCount;

    [ObservableProperty]
    private int _failedAccountCount;

    /// <summary>通知激活定位的目标账户 ID（由视图滚动到对应卡片并高亮）。</summary>
    [ObservableProperty]
    private string? _highlightedAccountId;

    [ObservableProperty]
    private bool _hasActiveFilters;

    /// <summary>“通知区域与启动”设置区（由 CompositionRoot 注入；独立 ViewModel 便于测试）。</summary>
    public TraySettingsViewModel? TraySettings { get; set; }

    /// <summary>“余额提醒”设置区（由 CompositionRoot 注入）。</summary>
    public NotificationSettingsViewModel? NotificationSettings { get; set; }

    /// <summary>“外观与语言”设置区（v0.6.0，由 CompositionRoot 注入）。</summary>
    public AppearanceSettingsViewModel? AppearanceSettings { get; set; }

    /// <summary>“数据管理”设置区（v0.6.0，由 CompositionRoot 注入）。</summary>
    public DataManagementViewModel? DataManagement { get; set; }

    /// <summary>数据洞察页 ViewModel（由 CompositionRoot 注入；导航外壳共享）。</summary>
    public InsightsViewModel? Insights { get; set; }

    /// <summary>关于页 ViewModel（由 CompositionRoot 注入）。</summary>
    public AboutViewModel? About { get; set; }

    /// <summary>主界面副标题，版本号取自集中元数据服务（AssemblyInformationalVersion）。</summary>
    public string SubtitleText { get; } =
        L10n.Format("Home.SubtitleFormat", AppInfo.DisplayVersion);

    /// <summary>主页空状态隐私说明（v0.6.0 起从资源取，避免硬编码）。</summary>
    public string HomePrivacyMessage { get; } =
        L10n.Get("Home.PrivacyMessageText");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccounts))]
    private bool _isLoading;

    /// <summary>是否正在执行“刷新全部账户”（托盘/主界面共用状态）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshAllCommand))]
    private bool _isRefreshingAll;

    /// <summary>是否有已加载账户（由账户集合派生，加载中不改变）。</summary>
    public bool HasAccounts => Accounts.Count > 0;

    public string AccountSummaryText =>
        L10n.Format("Home.AccountSummaryFormat", TotalAccountCount, LowBalanceAccountCount, FailedAccountCount);

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private StatusSeverity _statusSeverity;

    [ObservableProperty]
    private string _statusTitle;

    [ObservableProperty]
    private string _statusMessage;

    public AsyncRelayCommand AddAccountCommand { get; }

    public RelayCommand OpenCompactWindowCommand { get; }

    public AsyncRelayCommand RefreshAllCommand { get; }

    /// <summary>当前支持的 Provider 文本（设置页“应用信息”展示）。</summary>
    public string SupportedProvidersText { get; private set; } = "DeepSeek、OpenRouter";

    public MainViewModel(
        IAccountManager accountManager,
        IDialogService dialogs,
        AppLog log,
        IClipboardService clipboard,
        IUiThreadInvoker ui,
        Action? openCompactWindow = null,
        Func<string, CancellationToken, Task<DateTimeOffset?>>? snoozeReader = null)
    {
        _accountManager = accountManager;
        _dialogs = dialogs;
        _log = log;
        _clipboard = clipboard;
        _ui = ui;
        _openCompactWindow = openCompactWindow ?? (() => { });
        _snoozeReader = snoozeReader;

        StatusSeverity = StatusSeverity.Informational;
        StatusTitle = string.Empty;
        StatusMessage = string.Empty;
        AddAccountCommand = new AsyncRelayCommand(AddAccountAsync, () => !IsLoading);
        OpenCompactWindowCommand = new RelayCommand(() => _openCompactWindow());
        RefreshAllCommand = new AsyncRelayCommand(
            RefreshAllAsync,
            () => HasAccounts && !IsRefreshingAll);

        _accountManager.RefreshStarted += OnRefreshStarted;
        _accountManager.RefreshCompleted += OnRefreshCompleted;
        _accountManager.AccountsChanged += OnAccountsChanged;
    }

    partial void OnIsLoadingChanged(bool value) =>
        AddAccountCommand.NotifyCanExecuteChanged();

    partial void OnIsRefreshingAllChanged(bool value) =>
        RefreshAllCommand.NotifyCanExecuteChanged();

    partial void OnSelectedProviderFilterChanged(string value) => ApplyFilters();

    partial void OnSelectedStatusFilterChanged(AccountStatusFilter value)
    {
        HasActiveFilters = SelectedStatusFilter != AccountStatusFilter.All
            || !string.IsNullOrEmpty(SelectedProviderFilter);
        ApplyFilters();
    }

    /// <summary>切换到指定导航页面（不会重建账户状态，不重启调度器）。</summary>
    public void NavigateTo(AppPageKind page) => CurrentPage = page;

    /// <summary>把筛选恢复为“全部 Provider / 全部状态”（新增账户后与通知定位共用）。</summary>
    public void ResetFiltersToAll()
    {
        SelectedProviderFilter = string.Empty;
        SelectedStatusFilter = AccountStatusFilter.All;
    }

    /// <summary>应用启动时加载本地数据；文件损坏/迁移失败时显示恢复提示而不是崩溃。</summary>
    public async Task InitializeAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        IsStatusVisible = false;
        try
        {
            await _accountManager.LoadAsync(_lifetime.Token);
            await ReloadAccountsAsync(_lifetime.Token);

            foreach (var message in _accountManager.RecoveryMessages)
            {
                ShowStatus(StatusSeverity.Warning, L10n.Get("Status.DataRecoveredTitle"), message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"初始化本地数据失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, L10n.Get("Status.DataErrorTitle"), L10n.Get("Status.DataErrorCannotRead"));
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task AddAccountAsync()
    {
        var context = new AccountEditorContext
        {
            AccountId = null,
            Providers = _accountManager.Providers,
            InitialProviderId = _accountManager.Providers.FirstOrDefault()?.ProviderId ?? string.Empty,
            InitialDisplayName = string.Empty,
            HasStoredCredential = false,
            CredentialMode = null,
            InitialMonitoring = new MonitoringSettings(),
            CurrentMetrics = Array.Empty<BalanceMetric>(),
        };

        var result = await _dialogs.ShowAccountEditorAsync(context, _lifetime.Token);
        if (result is not { SaveRequested: true })
        {
            return;
        }

        try
        {
            await _accountManager.SaveAccountAsync(
                null,
                result.ProviderId,
                result.DisplayName,
                result.ApiKey,
                result.CredentialMode,
                result.Monitoring,
                _lifetime.Token,
                result.Notification);
            await ReloadAccountsAsync(_lifetime.Token);
            // 新账户可能被当前筛选隐藏：自动恢复为可见筛选，体验更直接。
            ResetFiltersToAll();
            await ReloadAccountsAsync(_lifetime.Token);
            ShowStatus(StatusSeverity.Success, L10n.Get("Status.AccountSavedTitle"), L10n.Format("Status.AccountAddedMessage", result.DisplayName));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"保存账户失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, L10n.Get("Status.DataErrorTitle"), L10n.Get("Status.AccountSaveFailed"));
        }
    }

    public async Task EditAccountAsync(string accountId)
    {
        var account = Accounts.FirstOrDefault(i => i.Account.AccountId == accountId)?.Account;
        if (account is null)
        {
            return;
        }

        var item = Accounts.FirstOrDefault(i => i.Account.AccountId == accountId);
        var context = new AccountEditorContext
        {
            AccountId = account.AccountId,
            Providers = _accountManager.Providers,
            InitialProviderId = account.ProviderId,
            InitialDisplayName = account.DisplayName,
            HasStoredCredential = account.HasCredential,
            CredentialMode = account.CredentialMode,
            InitialMonitoring = CloneMonitoring(account.Monitoring),
            InitialNotification = account.Notification,
            CurrentMetrics = item is { HasSnapshot: true } ? item.LatestMetricsForEditor : Array.Empty<BalanceMetric>(),
        };

        var result = await _dialogs.ShowAccountEditorAsync(context, _lifetime.Token);
        if (result is not { SaveRequested: true })
        {
            return;
        }

        try
        {
            await _accountManager.SaveAccountAsync(
                account.AccountId,
                result.ProviderId,
                result.DisplayName,
                string.IsNullOrWhiteSpace(result.ApiKey) ? null : result.ApiKey,
                result.CredentialMode,
                result.Monitoring,
                _lifetime.Token,
                result.Notification);
            await ReloadAccountsAsync(_lifetime.Token);
            ShowStatus(StatusSeverity.Success, L10n.Get("Status.AccountSavedTitle"), L10n.Format("Status.AccountUpdatedMessage", result.DisplayName));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"保存账户失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, L10n.Get("Status.DataErrorTitle"), L10n.Get("Status.AccountSaveFailed"));
        }
    }

    public async Task DeleteAccountAsync(string accountId)
    {
        var item = Accounts.FirstOrDefault(i => i.Account.AccountId == accountId);
        if (item is null)
        {
            return;
        }

        bool confirmed = await _dialogs.ConfirmDeleteAsync(
            item.DisplayName,
            item.ProviderDisplayName,
            _lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _accountManager.DeleteAccountAsync(accountId, _lifetime.Token);
            await ReloadAccountsAsync(_lifetime.Token);
            ShowStatus(StatusSeverity.Success, L10n.Get("Status.AccountDeletedTitle"), L10n.Format("Status.AccountDeletedMessage", item.DisplayName));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"删除账户失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, L10n.Get("Status.DataErrorTitle"), L10n.Get("Status.AccountDeleteFailed"));
        }
    }

    /// <summary>手动刷新单个账户；与自动刷新共用同一查询入口与并发保护。</summary>
    public async Task RefreshAccountAsync(string accountId)
    {
        var item = Accounts.FirstOrDefault(i => i.Account.AccountId == accountId);
        if (item is null)
        {
            return;
        }

        var result = await _accountManager.RefreshAccountAsync(
            accountId,
            BalanceQuerySource.Manual,
            _lifetime.Token);

        if (result.Error?.Kind == BalanceErrorKind.Busy)
        {
            ShowStatus(StatusSeverity.Informational, L10n.Get("Status.QueryInProgressTitle"), L10n.Get("Status.QueryInProgressMessage"));
            return;
        }

        await ApplyRefreshOutcomeAsync(item, result);

        if (result.IsSuccess)
        {
            ShowStatus(StatusSeverity.Success, L10n.Get("Status.QuerySuccessTitle"), L10n.Format("Status.QuerySuccessMessage", item.DisplayName));
        }
        else
        {
            ShowStatus(StatusSeverity.Error, L10n.Get("Status.QueryFailedTitle"), result.Error?.Message ?? L10n.Get("Common.UnknownError"));
        }
    }

    /// <summary>刷新全部账户：复用账户级并发锁，正在刷新的账户自动跳过。</summary>
    public async Task RefreshAllAsync()
    {
        if (IsRefreshingAll || !HasAccounts)
        {
            return;
        }

        IsRefreshingAll = true;
        try
        {
            await _accountManager.RefreshAllAccountsAsync(
                BalanceQuerySource.Manual,
                _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"刷新全部账户失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, L10n.Get("Status.RefreshFailedTitle"), L10n.Get("Status.RefreshFailedMessage"));
        }
        finally
        {
            IsRefreshingAll = false;
        }
    }

    /// <summary>复制指定账户的 API Key 到剪贴板，成功后安排延迟清理。</summary>
    public async Task CopyKeyAsync(string accountId)
    {
        var item = Accounts.FirstOrDefault(i => i.Account.AccountId == accountId);
        if (item is null || item.IsCopying)
        {
            return;
        }

        item.IsCopying = true;
        try
        {
            string? apiKey = await _accountManager.GetApiKeyAsync(accountId, _lifetime.Token);
            if (string.IsNullOrEmpty(apiKey))
            {
                ShowStatus(
                    StatusSeverity.Error,
                    L10n.Get("Status.CopyFailedTitle"),
                    L10n.Get("Status.CopyNoKeyMessage"));
                return;
            }

            await _clipboard.SetSensitiveTextAsync(
                apiKey,
                TimeSpan.FromSeconds(30),
                _lifetime.Token);

            ShowStatus(
                StatusSeverity.Success,
                L10n.Get("Status.CopiedTitle"),
                L10n.Get("Status.CopiedMessage"));
            AutoHideStatusAfter(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"复制 API Key 失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, L10n.Get("Status.CopyFailedTitle"), L10n.Get("Status.CopyFailedMessage"));
        }
        finally
        {
            item.IsCopying = false;
        }
    }

    /// <summary>打开账户余额历史对话框。</summary>
    public async Task ShowHistoryAsync(string accountId)
    {
        var item = Accounts.FirstOrDefault(i => i.Account.AccountId == accountId);
        if (item is null || item.IsHistoryOpen)
        {
            return;
        }

        item.IsHistoryOpen = true;
        try
        {
            await _dialogs.ShowHistoryAsync(accountId, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"打开历史记录失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, L10n.Get("Status.DataErrorTitle"), L10n.Get("Status.HistoryOpenFailed"));
        }
        finally
        {
            item.IsHistoryOpen = false;
        }
    }

    /// <summary>数据洞察页当前目标账户（由账户卡片“查看趋势”入口设置）。</summary>
    public string? InsightsTargetAccountId { get; set; }

    /// <summary>从账户卡片进入数据洞察页并预选该账户（不依赖账户名称作为主键）。</summary>
    public void OpenInsightsForAccount(string accountId)
    {
        InsightsTargetAccountId = accountId;
        NavigateTo(AppPageKind.Insights);
    }

    /// <summary>窗口关闭/应用退出时取消在途操作并解除事件订阅。</summary>
    public void Shutdown()
    {
        _accountManager.RefreshStarted -= OnRefreshStarted;
        _accountManager.RefreshCompleted -= OnRefreshCompleted;
        _accountManager.AccountsChanged -= OnAccountsChanged;
        // v0.6.0：退出时取消分析/导出/更新检查等在途操作。
        Insights?.Shutdown();
        About?.Shutdown();
        DataManagement?.Shutdown();
        AppearanceSettings?.Shutdown();
        _lifetime.Cancel();
    }

    private async Task ReloadAccountsAsync(CancellationToken cancellationToken)
    {
        var accounts = await _accountManager.GetAllAccountsAsync(cancellationToken);
        ProviderFilterOptions = new[]
            {
                new ProviderFilterOption(string.Empty, L10n.Get("Home.FilterAllProviders")),
            }
            .Concat(_accountManager.Providers.Select(p => new ProviderFilterOption(p.ProviderId, p.DisplayName)))
            .ToList();
        OnPropertyChanged(nameof(ProviderFilterOptions));

        Accounts.Clear();
        foreach (var account in accounts.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var record = await _accountManager.GetRecordAsync(account.AccountId, cancellationToken);
            string providerDisplayName =
                _accountManager.Providers.FirstOrDefault(p => p.ProviderId == account.ProviderId)?.DisplayName
                ?? account.ProviderId;

            var item = new AccountListItemViewModel(
                account,
                providerDisplayName,
                record,
                () => RefreshAccountAsync(account.AccountId),
                () => EditAccountAsync(account.AccountId),
                () => DeleteAccountAsync(account.AccountId),
                () => CopyKeyAsync(account.AccountId),
                () => ShowHistoryAsync(account.AccountId),
                () =>
                {
                    OpenInsightsForAccount(account.AccountId);
                    return Task.CompletedTask;
                });

            if (_snoozeReader is not null)
            {
                var snoozedUntil = await _snoozeReader(account.AccountId, cancellationToken);
                item.SnoozeSummaryText = snoozedUntil is { } until
                    ? L10n.Format("Home.SnoozeUntilFormat", until.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
                    : string.Empty;
            }

            Accounts.Add(item);
        }

        SupportedProvidersText = string.Join(
            "、",
            _accountManager.Providers.Select(p => p.DisplayName));
        OnPropertyChanged(nameof(SupportedProvidersText));
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(AccountSummaryText));
        RefreshSummary();
        ApplyFilters();
    }

    private async Task ApplyRefreshOutcomeAsync(
        AccountListItemViewModel item,
        BalanceQueryResult result)
    {
        var account = await _accountManager.GetAccountAsync(item.Account.AccountId, _lifetime.Token);
        var record = await _accountManager.GetRecordAsync(item.Account.AccountId, _lifetime.Token);

        if (result.IsSuccess && result.Snapshot is { } snapshot)
        {
            item.ApplySnapshot(snapshot);
        }
        else
        {
            item.ApplyError(result.Error);
        }

        if (account is not null)
        {
            item.RefreshDisplay();
        }

        RefreshSummary();
    }

    private void OnRefreshStarted(object? sender, AccountRefreshStartedEventArgs e)
    {
        _ui.Post(() =>
        {
            var item = Accounts.FirstOrDefault(i => i.Account.AccountId == e.AccountId);
            if (item is not null)
            {
                item.IsRefreshing = true;
            }
        });
    }

    private void OnRefreshCompleted(object? sender, AccountRefreshCompletedEventArgs e)
    {
        _ui.Post(() => _ = HandleRefreshCompletedAsync(e));
    }

    private void OnAccountsChanged(object? sender, EventArgs e)
    {
        _ui.Post(() => _ = ReloadAccountsAsync(_lifetime.Token));
    }

    /// <summary>把指定账户定位到主界面（通知激活共用）：必要时清除筛选并高亮卡片。</summary>
    public void FocusAccount(string accountId)
    {
        _ui.Post(() =>
        {
            // 通知激活必须自动导航到主页，并清除可能隐藏目标账户的筛选。
            NavigateTo(AppPageKind.Home);
            ResetFiltersToAll();

            foreach (var item in Accounts)
            {
                item.IsHighlighted =
                    string.Equals(item.Account.AccountId, accountId, StringComparison.OrdinalIgnoreCase);
            }

            HighlightedAccountId = accountId;
        });
    }

    /// <summary>显示普通信息提示（如通知点击的账户已被删除）。</summary>
    public void ShowPlainMessage(string title, string message) =>
        ShowStatus(StatusSeverity.Informational, title, message);

    private void ApplyFilters()
    {
        FilteredAccounts.Clear();
        foreach (var item in Accounts)
        {
            if (!string.IsNullOrEmpty(SelectedProviderFilter)
                && !string.Equals(item.Account.ProviderId, SelectedProviderFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!MatchesStatusFilter(item))
            {
                continue;
            }

            FilteredAccounts.Add(item);
        }
    }

    private bool MatchesStatusFilter(AccountListItemViewModel item) =>
        SelectedStatusFilter switch
        {
            AccountStatusFilter.Normal => item.StatusKind == AccountStatusKind.Normal,
            AccountStatusFilter.Low => item.StatusKind == AccountStatusKind.Low,
            AccountStatusFilter.Unknown => item.StatusKind == AccountStatusKind.Unknown,
            AccountStatusFilter.Failed => item.StatusKind == AccountStatusKind.Failed,
            _ => true,
        };

    private void RefreshSummary()
    {
        TotalAccountCount = Accounts.Count;
        LowBalanceAccountCount = Accounts.Count(a => a.StatusKind == AccountStatusKind.Low);
        FailedAccountCount = Accounts.Count(a => a.StatusKind == AccountStatusKind.Failed);
        OnPropertyChanged(nameof(AccountSummaryText));
    }

    private async Task HandleRefreshCompletedAsync(AccountRefreshCompletedEventArgs e)
    {
        var item = Accounts.FirstOrDefault(i => i.Account.AccountId == e.AccountId);
        if (item is null)
        {
            return;
        }

        item.IsRefreshing = false;
        await ApplyRefreshOutcomeAsync(item, e.Result);

        if (e.Source == BalanceQuerySource.Automatic)
        {
            if (e.Result.IsSuccess)
            {
                ShowStatus(StatusSeverity.Success, L10n.Get("Status.AutoRefreshDoneTitle"), L10n.Format("Status.QuerySuccessMessage", item.DisplayName));
            }
            else
            {
                ShowStatus(
                    StatusSeverity.Warning,
                    L10n.Get("Status.AutoRefreshFailedTitle"),
                    e.Result.Error?.Message ?? L10n.Get("Common.UnknownError"));
            }
        }
    }

    private void ShowStatus(StatusSeverity severity, string title, string message)
    {
        _statusGeneration++;
        StatusSeverity = severity;
        StatusTitle = title;
        StatusMessage = message;
        IsStatusVisible = true;
    }

    /// <summary>让状态提示在数秒后自动消失；期间出现新提示则不隐藏新提示。</summary>
    private void AutoHideStatusAfter(TimeSpan delay)
    {
        int generation = _statusGeneration;
        _ = AutoHideStatusCoreAsync(generation, delay);
    }

    private async Task AutoHideStatusCoreAsync(int generation, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (generation == _statusGeneration)
        {
            IsStatusVisible = false;
        }
    }

    private static MonitoringSettings CloneMonitoring(MonitoringSettings monitoring) =>
        new()
        {
            AutoRefreshEnabled = monitoring.AutoRefreshEnabled,
            RefreshIntervalMinutes = monitoring.RefreshIntervalMinutes,
            NextRefreshAtUtc = monitoring.NextRefreshAtUtc,
            Thresholds = monitoring.Thresholds
                .Select(t => new BalanceThresholdRule
                {
                    MetricId = t.MetricId,
                    DisplayName = t.DisplayName,
                    Unit = t.Unit,
                    IsEnabled = t.IsEnabled,
                    ThresholdAmount = t.ThresholdAmount,
                    CreatedAtUtc = t.CreatedAtUtc,
                    UpdatedAtUtc = t.UpdatedAtUtc,
                })
                .ToList(),
        };
}
