using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

/// <summary>
/// 数据洞察页：账户/指标/时间范围选择、趋势图、估算摘要、可折叠历史表。
/// 只做视图职责；数据加载与取消由 InsightsViewModel 管理。
/// </summary>
public sealed partial class InsightsPage : UserControl
{
    public InsightsPage()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)DataContext;
        set
        {
            DataContext = value;
            if (value?.Insights is { } insights)
            {
                // 绑定到共享的 InsightsViewModel（与主页同一账户服务）。
                DataContext = insights;
            }
        }
    }

    private void OnHistoryToggleChanged(object sender, RoutedEventArgs e)
    {
        bool show = HistoryToggle.IsChecked == true;
        HistoryList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        HistoryToggle.Content = show ? "隐藏历史数据表" : "显示历史数据表";
    }
}
