using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

/// <summary>
/// 主页：只承载账户余额与账户管理（标题/操作栏/汇总/筛选/卡片/空状态）。
/// 与设置页共享同一个 MainViewModel；生命周期与事件订阅不在本页复制。
/// </summary>
public sealed partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)DataContext;
        set
        {
            if (DataContext is MainViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            DataContext = value;
            if (value is not null)
            {
                value.PropertyChanged += OnViewModelPropertyChanged;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HighlightedAccountId)
            && ViewModel?.HighlightedAccountId is { } accountId)
        {
            ScrollToAccount(accountId);
        }
    }

    /// <summary>通知激活定位：滚动到目标卡片并短暂高亮。</summary>
    private void ScrollToAccount(string accountId)
    {
        var target = (ViewModel?.FilteredAccounts ?? new())
            .FirstOrDefault(item =>
                string.Equals(item.Account.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        // 等布局完成后再滚动（页面可能刚从设置页切换过来）。
        DispatcherQueue.TryEnqueue(() =>
        {
            AccountsList.ScrollIntoView(target);
            _ = ClearHighlightAfterAsync(target, TimeSpan.FromSeconds(6));
        });
    }

    private static async System.Threading.Tasks.Task ClearHighlightAfterAsync(
        AccountListItemViewModel item,
        TimeSpan delay)
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(delay);
            item.IsHighlighted = false;
        }
        catch
        {
            // 清除高亮失败不影响应用。
        }
    }
}
