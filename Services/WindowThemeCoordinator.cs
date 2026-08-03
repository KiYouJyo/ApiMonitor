using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ApiMonitor.Services;

/// <summary>
/// v0.6.0 主题统一：把主题偏好应用到窗口根元素（ElementTheme），
/// 并同步原生标题栏（AppWindowTitleBar）颜色，使标题栏、NavigationView
/// Pane 与页面内容属于同一视觉底层。
///
/// 标题栏原生颜色不会自动继承 XAML ThemeResource，必须由本协调器更新；
/// 主题切换、窗口显示、ActualThemeChanged 时都会同步。高对比度时回退
/// 系统默认标题栏（不强制自定义颜色），优先保证可读性。
/// </summary>
public sealed class WindowThemeCoordinator
{
    private readonly IAppearanceService _appearance;
    private readonly List<RegisteredWindow> _windows = new();

    private sealed class RegisteredWindow
    {
        public required Microsoft.UI.Windowing.AppWindow AppWindow { get; init; }

        public required FrameworkElement Root { get; init; }

        public required bool IsMainWindow { get; init; }
    }

    public WindowThemeCoordinator(IAppearanceService appearance)
    {
        _appearance = appearance;
    }

    /// <summary>注册窗口根元素（主窗口与紧凑窗口各一次）；立即应用当前主题。</summary>
    public void RegisterWindow(
        Microsoft.UI.Windowing.AppWindow appWindow,
        FrameworkElement root,
        bool isMainWindow)
    {
        lock (_windows)
        {
            _windows.Add(new RegisteredWindow
            {
                AppWindow = appWindow,
                Root = root,
                IsMainWindow = isMainWindow,
            });
        }

        ApplyThemeToRoot(root);
        SyncTitleBar(appWindow);
    }

    public void UnregisterWindow(FrameworkElement root)
    {
        lock (_windows)
        {
            _windows.RemoveAll(w => ReferenceEquals(w.Root, root));
        }
    }

    /// <summary>应用当前主题偏好到所有已注册窗口（主题切换时调用）。</summary>
    public void ApplyTheme(AppThemePreference theme)
    {
        lock (_windows)
        {
            foreach (var window in _windows.ToArray())
            {
                ApplyThemeToRoot(window.Root);
                SyncTitleBar(window.AppWindow);
            }
        }
    }

    private void ApplyThemeToRoot(FrameworkElement root)
    {
        try
        {
            root.RequestedTheme = _appearance.Theme switch
            {
                AppThemePreference.Light => ElementTheme.Light,
                AppThemePreference.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
        catch
        {
            // 主题应用失败不影响应用。
        }
    }

    /// <summary>
    /// 同步原生标题栏颜色。颜色取自语义资源 AppShellBackgroundBrush；
    /// 通过 Root.Resources 在元素主题下解析（主题切换后立即更新）。
    /// 高对比度（UISettings.AdvancedEffectsEnabled 或高对比度模式）回退系统默认。
    /// </summary>
    private void SyncTitleBar(Microsoft.UI.Windowing.AppWindow appWindow)
    {
        try
        {
            var titleBar = appWindow.TitleBar;
            if (titleBar is null)
            {
                return;
            }

            // 高对比度：不强制自定义颜色，使用系统默认标题栏。
            if (IsHighContrast())
            {
                titleBar.BackgroundColor = null;
                titleBar.ButtonBackgroundColor = null;
                titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonPressedBackgroundColor = null;
                titleBar.ButtonForegroundColor = null;
                return;
            }

            Color? shell = GetThemeColor("AppShellBackgroundBrush");
            Color? primary = GetThemeColor("AppPrimaryTextBrush");
            Color? navSelected = GetThemeColor("AppNavigationSelectedBrush");
            Color? hover = GetThemeColor("AppCardBackgroundBrush");
            Color? pressed = GetThemeColor("AppCardBorderBrush");

            titleBar.BackgroundColor = shell;
            titleBar.InactiveBackgroundColor = shell;
            titleBar.ButtonBackgroundColor = shell;
            titleBar.ButtonInactiveBackgroundColor = shell;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonPressedBackgroundColor = pressed;
            titleBar.ButtonForegroundColor = primary;
            titleBar.ButtonInactiveForegroundColor = primary;
        }
        catch
        {
            // 标题栏同步失败不影响应用。
        }
    }

    private Color? GetThemeColor(string resourceKey)
    {
        try
        {
            // 语义资源在 Application.Resources 的 ThemeDictionaries 中；
            // 按当前元素主题（浅色/深色）选择字典。
            string dictKey = _appearance.Theme switch
            {
                AppThemePreference.Light => "Light",
                AppThemePreference.Dark => "Dark",
                _ => ActualThemeName(),
            };

            if (Application.Current?.Resources.ThemeDictionaries.TryGetValue(dictKey, out var dictValue) == true
                && dictValue is ResourceDictionary dict
                && dict.TryGetValue(resourceKey, out var value) == true
                && value is SolidColorBrush brush)
            {
                return brush.Color;
            }
        }
        catch
        {
            // 回退 null。
        }

        return null;
    }

    private static string ActualThemeName()
    {
        try
        {
            return Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? "Dark"
                : "Light";
        }
        catch
        {
            return "Light";
        }
    }

    private static bool IsHighContrast()
    {
        try
        {
            var accessibility = new Windows.UI.ViewManagement.AccessibilitySettings();
            return accessibility.HighContrast;
        }
        catch
        {
            return false;
        }
    }
}
