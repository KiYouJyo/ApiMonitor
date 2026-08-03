using System.Collections.ObjectModel;
using System.Reflection;
using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

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
    private readonly CancellationTokenSource _lifetime = new();
    private int _statusGeneration;

    public ObservableCollection<AccountListItemViewModel> Accounts { get; } = new();

    /// <summary>主界面副标题，版本号取自程序集元数据，避免与包版本脱节。</summary>
    public string SubtitleText { get; } =
        $"查询并记录你自己的 API 账户余额（v{GetAppVersion()}，当前支持 DeepSeek）。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccounts))]
    private bool _isLoading;

    /// <summary>是否有已加载账户（由账户集合派生，加载中不改变）。</summary>
    public bool HasAccounts => Accounts.Count > 0;

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private StatusSeverity _statusSeverity;

    [ObservableProperty]
    private string _statusTitle;

    [ObservableProperty]
    private string _statusMessage;

    public AsyncRelayCommand AddAccountCommand { get; }

    public MainViewModel(
        IAccountManager accountManager,
        IDialogService dialogs,
        AppLog log,
        IClipboardService clipboard,
        IUiThreadInvoker ui)
    {
        _accountManager = accountManager;
        _dialogs = dialogs;
        _log = log;
        _clipboard = clipboard;
        _ui = ui;

        StatusSeverity = StatusSeverity.Informational;
        StatusTitle = string.Empty;
        StatusMessage = string.Empty;
        AddAccountCommand = new AsyncRelayCommand(AddAccountAsync, () => !IsLoading);

        _accountManager.RefreshStarted += OnRefreshStarted;
        _accountManager.RefreshCompleted += OnRefreshCompleted;
    }

    partial void OnIsLoadingChanged(bool value) =>
        AddAccountCommand.NotifyCanExecuteChanged();

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
                ShowStatus(StatusSeverity.Warning, "本地数据已恢复", message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"初始化本地数据失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, "本地数据错误", "无法读取本地数据，应用将继续以空数据启动。");
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
            InitialMonitoring = new MonitoringSettings(),
            CurrentBalances = Array.Empty<BalanceAmount>(),
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
                result.Monitoring,
                _lifetime.Token);
            await ReloadAccountsAsync(_lifetime.Token);
            ShowStatus(StatusSeverity.Success, "账户已保存", $"账户“{result.DisplayName}”已添加。");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"保存账户失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, "本地数据错误", "保存账户失败，请稍后重试。");
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
            InitialMonitoring = CloneMonitoring(account.Monitoring),
            CurrentBalances = item is { HasSnapshot: true } ? item.LatestBalancesForEditor : Array.Empty<BalanceAmount>(),
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
                result.Monitoring,
                _lifetime.Token);
            await ReloadAccountsAsync(_lifetime.Token);
            ShowStatus(StatusSeverity.Success, "账户已保存", $"账户“{result.DisplayName}”已更新。");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"保存账户失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, "本地数据错误", "保存账户失败，请稍后重试。");
        }
    }

    public async Task DeleteAccountAsync(string accountId)
    {
        var item = Accounts.FirstOrDefault(i => i.Account.AccountId == accountId);
        if (item is null)
        {
            return;
        }

        bool confirmed = await _dialogs.ConfirmDeleteAsync(item.DisplayName, _lifetime.Token);
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _accountManager.DeleteAccountAsync(accountId, _lifetime.Token);
            await ReloadAccountsAsync(_lifetime.Token);
            ShowStatus(StatusSeverity.Success, "账户已删除", $"账户“{item.DisplayName}”及其凭据、余额快照与历史记录已删除。");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"删除账户失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, "本地数据错误", "删除账户失败，请稍后重试。");
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
            ShowStatus(StatusSeverity.Informational, "查询进行中", "该账户正在查询，请稍候。");
            return;
        }

        await ApplyRefreshOutcomeAsync(item, result);

        if (result.IsSuccess)
        {
            ShowStatus(StatusSeverity.Success, "查询成功", $"账户“{item.DisplayName}”的余额已更新。");
        }
        else
        {
            ShowStatus(StatusSeverity.Error, "查询失败", result.Error?.Message ?? "未知错误。");
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
                    "复制失败",
                    "未找到该账户保存的 API Key，请重新编辑账户并保存密钥。");
                return;
            }

            await _clipboard.SetSensitiveTextAsync(
                apiKey,
                TimeSpan.FromSeconds(30),
                _lifetime.Token);

            ShowStatus(
                StatusSeverity.Success,
                "已复制",
                "API Key 已复制，30 秒后将尝试从剪贴板清除");
            AutoHideStatusAfter(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"复制 API Key 失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, "复制失败", "复制 API Key 失败，请重试。");
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
            ShowStatus(StatusSeverity.Error, "本地数据错误", "打开历史记录失败，请稍后重试。");
        }
        finally
        {
            item.IsHistoryOpen = false;
        }
    }

    /// <summary>窗口关闭/应用退出时取消在途操作并解除事件订阅。</summary>
    public void Shutdown()
    {
        _accountManager.RefreshStarted -= OnRefreshStarted;
        _accountManager.RefreshCompleted -= OnRefreshCompleted;
        _lifetime.Cancel();
    }

    private async Task ReloadAccountsAsync(CancellationToken cancellationToken)
    {
        var accounts = await _accountManager.GetAllAccountsAsync(cancellationToken);

        Accounts.Clear();
        foreach (var account in accounts.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var record = await _accountManager.GetRecordAsync(account.AccountId, cancellationToken);
            string providerDisplayName =
                _accountManager.Providers.FirstOrDefault(p => p.ProviderId == account.ProviderId)?.DisplayName
                ?? account.ProviderId;

            Accounts.Add(new AccountListItemViewModel(
                account,
                providerDisplayName,
                record,
                () => RefreshAccountAsync(account.AccountId),
                () => EditAccountAsync(account.AccountId),
                () => DeleteAccountAsync(account.AccountId),
                () => CopyKeyAsync(account.AccountId),
                () => ShowHistoryAsync(account.AccountId)));
        }

        OnPropertyChanged(nameof(HasAccounts));
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
                ShowStatus(StatusSeverity.Success, "自动刷新完成", $"账户“{item.DisplayName}”的余额已更新。");
            }
            else
            {
                ShowStatus(
                    StatusSeverity.Warning,
                    "自动刷新失败",
                    e.Result.Error?.Message ?? "未知错误。");
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
                    Currency = t.Currency,
                    IsEnabled = t.IsEnabled,
                    ThresholdAmount = t.ThresholdAmount,
                    CreatedAtUtc = t.CreatedAtUtc,
                    UpdatedAtUtc = t.UpdatedAtUtc,
                })
                .ToList(),
        };

    private static string GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? "?"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
