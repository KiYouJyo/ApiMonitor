using System.Collections.ObjectModel;
using ApiMonitor.Helpers;
using ApiMonitor.Models;
using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>账户余额历史对话框的 ViewModel。</summary>
public sealed partial class BalanceHistoryViewModel : ObservableObject
{
    private readonly IAccountManager _accountManager;
    private readonly string _accountId;
    private readonly AppLog? _log;

    public ObservableCollection<BalanceHistoryDisplayItem> Items { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private bool _isConfirmingClear;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    public AsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand BeginClearCommand { get; }

    public IAsyncRelayCommand ConfirmClearCommand { get; }

    public IRelayCommand CancelClearCommand { get; }

    public BalanceHistoryViewModel(IAccountManager accountManager, string accountId, AppLog? log = null)
    {
        _accountManager = accountManager;
        _accountId = accountId;
        _log = log;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        BeginClearCommand = new RelayCommand(() => IsConfirmingClear = true);
        CancelClearCommand = new RelayCommand(() => IsConfirmingClear = false);
        ConfirmClearCommand = new AsyncRelayCommand(ClearAndReloadAsync);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        HasStatus = false;
        try
        {
            var history = (await _accountManager.GetHistoryAsync(_accountId, cancellationToken))
                .OrderByDescending(h => h.SucceededAtUtc)
                .ToList();
            Items.Clear();
            foreach (var entry in history)
            {
                Items.Add(new BalanceHistoryDisplayItem(entry));
            }

            HasItems = Items.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error($"加载历史记录失败: {ex.GetType().Name}");
            StatusMessage = "无法读取历史记录，请稍后重试。";
            HasStatus = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ClearAndReloadAsync()
    {
        IsConfirmingClear = false;
        try
        {
            await _accountManager.ClearHistoryAsync(_accountId, CancellationToken.None);
            await LoadAsync();
            StatusMessage = "历史记录已清除。";
            HasStatus = true;
        }
        catch (Exception ex)
        {
            _log?.Error($"清除历史失败: {ex.GetType().Name}");
            StatusMessage = "清除历史记录失败，请稍后重试。";
            HasStatus = true;
        }
    }
}

/// <summary>历史列表中单条记录的展示模型。</summary>
public sealed class BalanceHistoryDisplayItem
{
    public string TimeText { get; }

    public string SourceText { get; }

    public string AvailabilityText { get; }

    public IReadOnlyList<string> BalanceLines { get; }

    public BalanceHistoryDisplayItem(BalanceHistoryEntry entry)
    {
        TimeText = entry.SucceededAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        SourceText = entry.Source == BalanceQuerySource.Automatic ? "自动" : "手动";
        AvailabilityText = entry.IsAvailable ? "可用" : "不可用";
        BalanceLines = entry.Balances
            .Select(b =>
                $"{b.Currency} 总额 {BalanceFormatter.Format(b.TotalBalance)} · " +
                $"赠送 {BalanceFormatter.Format(b.GrantedBalance)} · " +
                $"充值 {BalanceFormatter.Format(b.ToppedUpBalance)}")
            .ToList();
    }
}
