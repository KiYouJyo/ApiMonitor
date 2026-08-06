using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

/// <summary>
/// 主窗口导航外壳：负责页面切换与 NavigationView 选中项同步。
/// 生命周期（初始化、调度、事件订阅）仍留在 CompositionRoot/App 与
/// 共享的 MainViewModel 中，页面切换不会重建或重复订阅。
/// 数据洞察页在首次进入时才加载账户与历史（不阻塞启动）。
/// </summary>
public sealed partial class MainPage : UserControl
{
    private bool _insightsInitialized;

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
            HomePageControl.ViewModel = value;
            SettingsPageControl.ViewModel = value;
            InsightsPageControl.ViewModel = value;
            AboutPageControl.ViewModel = value;
            OnboardingPageControl.ViewModel = value;
            if (value is not null)
            {
                value.PropertyChanged += OnViewModelPropertyChanged;
                SyncNavigationSelection(value.CurrentPage);
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage) && ViewModel is not null)
        {
            SyncNavigationSelection(ViewModel.CurrentPage);
        }
    }

    private void SyncNavigationSelection(AppPageKind page)
    {
        string target = page switch
        {
            AppPageKind.Insights => "Insights",
            AppPageKind.Settings => "Settings",
            AppPageKind.About => "About",
            AppPageKind.Onboarding => "Home",
            _ => "Home",
        };

        foreach (var item in RootNavigation.MenuItems)
        {
            if (item is NavigationViewItem nvi
                && string.Equals(nvi.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                RootNavigation.SelectedItem = nvi;
                return;
            }
        }

        foreach (var item in RootNavigation.FooterMenuItems)
        {
            if (item is NavigationViewItem nvi
                && string.Equals(nvi.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                RootNavigation.SelectedItem = nvi;
                return;
            }
        }
    }

    private void OnRootNavigationLoaded(object sender, RoutedEventArgs e) =>
        SyncNavigationSelection(ViewModel?.CurrentPage ?? AppPageKind.Home);

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item
            || item.Tag is not string tag
            || ViewModel is null)
        {
            return;
        }

        AppPageKind page = tag.ToLowerInvariant() switch
        {
            "insights" => AppPageKind.Insights,
            "settings" => AppPageKind.Settings,
            "about" => AppPageKind.About,
            _ => AppPageKind.Home,
        };

        // 首次进入数据洞察：加载账户列表（按需加载，不阻塞启动）。
        if (page == AppPageKind.Insights && !_insightsInitialized)
        {
            _insightsInitialized = true;
            if (ViewModel.Insights is { } insightsVm)
            {
                _ = insightsVm.LoadAccountsAsync();
            }
        }

        // 从账户卡片“查看趋势”进入：预选目标账户。
        if (page == AppPageKind.Insights
            && ViewModel.InsightsTargetAccountId is { } targetAccountId
            && ViewModel.Insights is { } insights)
        {
            insights.TargetAccountId = targetAccountId;
            insights.SelectAccount(targetAccountId);
            ViewModel.InsightsTargetAccountId = null;
        }

        // 离开数据洞察：释放大型集合。
        if (page != AppPageKind.Insights)
        {
            ViewModel.Insights?.Release();
        }

        ViewModel.NavigateTo(page);
    }
}
