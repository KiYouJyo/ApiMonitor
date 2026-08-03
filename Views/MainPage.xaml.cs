using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

public sealed partial class MainPage : UserControl
{
    public MainPage()
    {
        InitializeComponent();
    }

    public MainViewModel ViewModel
    {
        get => (MainViewModel)DataContext;
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
        if (e.PropertyName != nameof(MainViewModel.HighlightedAccountId)
            || ViewModel?.HighlightedAccountId is not { } accountId)
        {
            return;
        }

        // 定位并短暂高亮通知对应的账户卡片；高亮状态随后由主界面清除。
        var list = FindName("AccountsList") as ListView;
        if (list is null)
        {
            return;
        }

        foreach (var item in ViewModel.FilteredAccounts)
        {
            if (string.Equals(item.Account.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
            {
                list.ScrollIntoView(item);
                _ = ClearHighlightAfterAsync(item, TimeSpan.FromSeconds(6));
                break;
            }
        }
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
