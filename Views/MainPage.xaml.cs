using ApiMonitor.ViewModels;
using ApiMonitor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

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
    private SplitView? _navigationSplitView;
    private Window? _parentWindow;
    private bool _isWindowActive = true;

    private void HookPaneBackground()
    {
        RootNavigation.PaneOpening += OnPaneOpening;
        ActualThemeChanged += OnShellThemeChanged;
        QueuePaneBackgroundUpdate();
    }

    private void OnPaneOpening(NavigationView sender, object args) => QueuePaneBackgroundUpdate();
    private void OnShellThemeChanged(FrameworkElement sender, object args) => QueuePaneBackgroundUpdate();

    private void OnParentWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        QueuePaneBackgroundUpdate();
    }

    private void QueuePaneBackgroundUpdate() => DispatcherQueue.TryEnqueue(ApplyPaneBackground);

    private void ApplyPaneBackground()
    {
        _navigationSplitView ??= FindDescendant<SplitView>(RootNavigation);
        string themeKey = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            ? "HighContrast"
            : RootNavigation.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        string brushKey = _isWindowActive
            ? "ShellNavigationPaneBackgroundBrush"
            : "ShellNavigationPaneInactiveBackgroundBrush";
        if (_navigationSplitView is null) return;
        if (Application.Current.Resources.ThemeDictionaries[themeKey] is ResourceDictionary resources
            && resources.TryGetValue(brushKey, out var value) && value is Brush brush)
        {
            _navigationSplitView.PaneBackground = brush;
        }
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } matchBelow) return matchBelow;
        }
        return null;
    }

    /// <summary>Unhook the previous page before a language-driven view rebuild.</summary>
    public void DetachShellEvents()
    {
        if (DataContext is MainViewModel model)
            model.PropertyChanged -= OnViewModelPropertyChanged;
        if (_parentWindow is not null)
            _parentWindow.Activated -= OnParentWindowActivated;
        RootNavigation.PaneOpening -= OnPaneOpening;
        ActualThemeChanged -= OnShellThemeChanged;
    }

    /// <summary>Update visible and cached navigation strings without recreating any page.</summary>
    public void RefreshLocalizedContent()
    {
        Loc.RefreshTree(this);
        foreach (var item in RootNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            Loc.RefreshElement(item);
        }
        foreach (var item in RootNavigation.FooterMenuItems.OfType<NavigationViewItem>())
        {
            Loc.RefreshElement(item);
        }
    }

    public MainPage()
    {
        InitializeComponent();
        HookPaneBackground();
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
        bool onboarding = page == AppPageKind.Onboarding;
        RootNavigation.IsPaneVisible = !onboarding;
        RootNavigation.IsPaneToggleButtonVisible = !onboarding;
        if (onboarding)
        {
            // No menu selection while the first-run experience owns the content.
            // Keeping Home selected could route SelectionChanged back to Home.
            RootNavigation.SelectedItem = null;
            return;
        }

        string target = page switch
        {
            AppPageKind.Insights => "Insights",
            AppPageKind.Settings => "Settings",
            AppPageKind.About => "About",
            AppPageKind.Onboarding => "Home",
            _ => "Home",
        };

        if (page == AppPageKind.Settings)
        {
            RootNavigation.SelectedItem = RootNavigation.SettingsItem;
            return;
        }

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

    private void OnRootNavigationLoaded(object sender, RoutedEventArgs e)
    {
        if (_parentWindow is null && Application.Current is ApiMonitor.App app
            && app.MainWindow is { } mainWindow)
        {
            _parentWindow = mainWindow;
            _parentWindow.Activated += OnParentWindowActivated;
        }

        if (RootNavigation.SettingsItem is NavigationViewItem settings)
        {
            var title = L10n.Get("Nav.Settings");
            settings.Content = title;
            AutomationProperties.SetName(settings, title);
            ToolTipService.SetToolTip(settings, title);
        }

        SyncNavigationSelection(ViewModel?.CurrentPage ?? AppPageKind.Home);
        QueuePaneBackgroundUpdate();
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (ViewModel is null) return;
        string tag = args.IsSettingsInvoked
            ? "settings"
            : (args.InvokedItemContainer as NavigationViewItem)?.Tag as string ?? "home";

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
