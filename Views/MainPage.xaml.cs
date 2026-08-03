using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

/// <summary>
/// 主窗口导航外壳：负责页面切换与 NavigationView 选中项同步。
/// 生命周期（初始化、调度、事件订阅）仍留在 CompositionRoot/App 与
/// 共享的 MainViewModel 中，页面切换不会重建或重复订阅。
/// </summary>
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
            HomePageControl.ViewModel = value;
            SettingsPageControl.ViewModel = value;
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
        string target = page == AppPageKind.Home ? "Home" : "Settings";
        foreach (var item in RootNavigation.MenuItems)
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

        ViewModel.NavigateTo(
            string.Equals(tag, "Settings", StringComparison.OrdinalIgnoreCase)
                ? AppPageKind.Settings
                : AppPageKind.Home);
    }
}
