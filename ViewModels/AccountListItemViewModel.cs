using ApiBalanceMonitor.Helpers;
using ApiBalanceMonitor.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiBalanceMonitor.ViewModels;

/// <summary>账户卡片视图模型，持有刷新/编辑/删除命令与展示状态。</summary>
public sealed partial class AccountListItemViewModel : ObservableObject
{
    private readonly Func<Task> _refreshAsync;
    private readonly Func<Task> _editAsync;
    private readonly Func<Task> _deleteAsync;
    private readonly Func<Task> _copyAsync;

    public ApiAccount Account { get; }

    public string ProviderDisplayName { get; }

    public string DisplayName => Account.DisplayName;

    public bool HasStoredCredential => Account.HasCredential;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _isCopying;

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

    public AccountListItemViewModel(
        ApiAccount account,
        string providerDisplayName,
        AccountBalanceRecord? record,
        Func<Task> refreshAsync,
        Func<Task> editAsync,
        Func<Task> deleteAsync,
        Func<Task> copyAsync)
    {
        Account = account;
        ProviderDisplayName = providerDisplayName;
        _refreshAsync = refreshAsync;
        _editAsync = editAsync;
        _deleteAsync = deleteAsync;
        _copyAsync = copyAsync;

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
    }

    partial void OnIsRefreshingChanged(bool value) =>
        RefreshCommand.NotifyCanExecuteChanged();

    partial void OnIsCopyingChanged(bool value) =>
        CopyKeyCommand.NotifyCanExecuteChanged();

    partial void OnLastSuccessTextChanged(string value) =>
        OnPropertyChanged(nameof(LastSuccessLine));

    partial void OnLastErrorTextChanged(string value) =>
        HasLastError = !string.IsNullOrEmpty(value);

    public void ApplySnapshot(BalanceSnapshot snapshot)
    {
        IsAvailable = snapshot.IsAvailable;
        HasSnapshot = true;
        AvailabilityText = snapshot.IsAvailable ? "可用" : "不可用";
        LastSuccessText = FormatTime(snapshot.RetrievedAt);
        LastErrorText = string.Empty;
        BalanceLines = snapshot.Balances
            .Select(b => new BalanceLine(
                b.Currency,
                BalanceFormatter.Format(b.TotalBalance),
                BalanceFormatter.Format(b.GrantedBalance),
                BalanceFormatter.Format(b.ToppedUpBalance)))
            .ToList();
    }

    public void ApplyError(BalanceQueryError? error)
    {
        LastErrorText = error?.Message ?? "查询失败。";
        if (!HasSnapshot)
        {
            AvailabilityText = "不可用";
        }
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
