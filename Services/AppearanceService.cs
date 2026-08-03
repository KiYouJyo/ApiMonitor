namespace ApiMonitor.Services;

/// <summary>
/// v0.6.0：外观服务（纯逻辑，不依赖 UI 框架）。
/// 维护当前主题偏好并通知订阅者（CompositionRoot 把主题应用到各窗口根元素）。
/// 主题切换不重建账户服务、不中断自动刷新、不重建托盘图标、不创建第二个窗口。
/// </summary>
public interface IAppearanceService
{
    /// <summary>当前主题偏好（跟随系统/浅色/深色）。</summary>
    AppThemePreference Theme { get; }

    /// <summary>主题偏好变化事件（订阅者负责应用到窗口根元素）。</summary>
    event Action<AppThemePreference>? ThemeChanged;

    /// <summary>设置并应用当前主题偏好（启动时与设置变更后调用）。</summary>
    void ApplyTheme(AppThemePreference theme);
}

public sealed class AppearanceService : IAppearanceService
{
    private AppThemePreference _theme = AppThemePreference.System;

    public AppThemePreference Theme => _theme;

    public event Action<AppThemePreference>? ThemeChanged;

    public void ApplyTheme(AppThemePreference theme)
    {
        _theme = theme;
        ThemeChanged?.Invoke(theme);
    }
}
