using System.Collections.ObjectModel;
using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiBalanceMonitor.ViewModels;

/// <summary>
/// 主界面 ViewModel。所有网络与持久化操作都通过服务接口完成；
/// 异步命令在 UI 线程发起，延续默认回到 UI SynchronizationContext。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAccountManager _accountManager;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly AppLog _log;
    private readonly CancellationTokenSource _lifetime = new();
    private int _statusGeneration;

    public ObservableCollection<AccountListItemViewModel> Accounts { get; } = new();

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
        IClipboardService clipboard)
    {
        _accountManager = accountManager;
        _dialogs = dialogs;
        _log = log;
        _clipboard = clipboard;

        StatusSeverity = StatusSeverity.Informational;
        StatusTitle = string.Empty;
        StatusMessage = string.Empty;
        AddAccountCommand = new AsyncRelayCommand(AddAccountAsync, () => !IsLoading);
    }

    partial void OnIsLoadingChanged(bool value) =>
        AddAccountCommand.NotifyCanExecuteChanged();

    /// <summary>应用启动时加载本地数据；文件损坏时显示恢复提示而不是崩溃。</summary>
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

        var context = new AccountEditorContext
        {
            AccountId = account.AccountId,
            Providers = _accountManager.Providers,
            InitialProviderId = account.ProviderId,
            InitialDisplayName = account.DisplayName,
            HasStoredCredential = account.HasCredential,
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
            ShowStatus(StatusSeverity.Success, "账户已删除", $"账户“{item.DisplayName}”及其凭据与余额快照已删除。");
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

    /// <summary>手动刷新单个账户；执行期间防止重复点击。</summary>
    public async Task RefreshAccountAsync(string accountId)
    {
        var item = Accounts.FirstOrDefault(i => i.Account.AccountId == accountId);
        if (item is null || item.IsRefreshing)
        {
            return;
        }

        item.IsRefreshing = true;
        try
        {
            var result = await _accountManager.RefreshAccountAsync(accountId, _lifetime.Token);
            if (result.IsSuccess && result.Snapshot is { } snapshot)
            {
                item.ApplySnapshot(snapshot);
                ShowStatus(StatusSeverity.Success, "查询成功", $"账户“{item.DisplayName}”的余额已更新。");
            }
            else
            {
                item.ApplyError(result.Error);
                ShowStatus(StatusSeverity.Error, "查询失败", result.Error?.Message ?? "未知错误。");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"刷新账户失败: {ex.GetType().Name}");
            ShowStatus(StatusSeverity.Error, "查询失败", "发生意外错误，请稍后重试。");
        }
        finally
        {
            item.IsRefreshing = false;
        }
    }

    /// <summary>窗口关闭/应用退出时取消在途操作。</summary>
    public void Shutdown() => _lifetime.Cancel();

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
                () => CopyKeyAsync(account.AccountId)));
        }

        OnPropertyChanged(nameof(HasAccounts));
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
}
