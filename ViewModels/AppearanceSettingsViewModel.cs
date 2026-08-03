using ApiMonitor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApiMonitor.ViewModels;

/// <summary>主题选项。</summary>
public sealed record ThemeOption(AppThemePreference Theme, string DisplayName);

/// <summary>语言选项。</summary>
public sealed record LanguageOption(AppLanguagePreference Language, string DisplayName);

/// <summary>
/// v0.6.0：设置页“外观与语言”区 ViewModel。
/// 主题选择立即生效（AppearanceService 应用到所有窗口根元素）并持久化；
/// 语言切换保存 PrimaryLanguageOverride 后提示重启（立即重启或稍后）。
/// </summary>
public sealed partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly IAppearanceSettingsStore _store;
    private readonly IAppearanceService _appearance;
    private readonly ILanguageService _language;
    private readonly Func<bool> _requestRestart;
    private readonly Func<Task<bool>>? _confirmRestartAsync;
    private readonly AppLog? _log;

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = new[]
    {
        new ThemeOption(AppThemePreference.System, "跟随系统"),
        new ThemeOption(AppThemePreference.Light, "浅色"),
        new ThemeOption(AppThemePreference.Dark, "深色"),
    };

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = new[]
    {
        new LanguageOption(AppLanguagePreference.System, "跟随系统"),
        new LanguageOption(AppLanguagePreference.ZhCn, "简体中文"),
        new LanguageOption(AppLanguagePreference.EnUs, "English"),
        new LanguageOption(AppLanguagePreference.JaJp, "日本語"),
    };

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private bool _isLanguageBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    /// <summary>启动初始化中：跳过主题/语言切换触发的立即保存，避免启动期并发写盘。</summary>
    private bool _isInitializing;

    public AppearanceSettingsViewModel(
        IAppearanceSettingsStore store,
        IAppearanceService appearance,
        ILanguageService language,
        Func<bool>? requestRestart = null,
        Func<Task<bool>>? confirmRestartAsync = null,
        AppLog? log = null)
    {
        _store = store;
        _appearance = appearance;
        _language = language;
        _requestRestart = requestRestart ?? (() => false);
        _confirmRestartAsync = confirmRestartAsync;
        _log = log ?? new AppLog(System.IO.Path.GetTempPath());

        // 构造期直接赋值后备字段，不触发 OnSelectedThemeChanged/OnSelectedLanguageChanged
        // （否则会在 InitializeAsync 读取持久化设置之前把默认值 System 写回文件，
        // 导致主题/语言选择永远无法恢复——v0.6.0 主题修复）。
        _isInitializing = true;
        try
        {
            _selectedTheme = ThemeOptions[0];
            _selectedLanguage = LanguageOptions[0];
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _isInitializing = true;
        try
        {
            var settings = await _store.LoadAsync(cancellationToken);
            var theme = ThemeOptions.FirstOrDefault(t => t.Theme.ToString() == settings.Theme) ?? ThemeOptions[0];
            var language = LanguageOptions.FirstOrDefault(l => l.Language.ToString() == settings.Language)
                ?? LanguageOptions[0];

            SelectedTheme = theme;
            SelectedLanguage = language;

            // 应用持久化的主题与语言（启动时）。
            _appearance.ApplyTheme(theme.Theme);
            _language.ApplyLanguage(language.Language);
        }
        catch (Exception ex)
        {
            _log?.Error($"初始化外观设置失败: {ex.GetType().Name}");
        }
        finally
        {
            _isInitializing = false;
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        _appearance.ApplyTheme(value.Theme);
        if (!_isInitializing)
        {
            _ = SaveAsync();
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (_isInitializing)
        {
            return;
        }

        // 语言切换：保存 PrimaryLanguageOverride，提示重启，提供“立即重启/稍后”。
        _ = HandleLanguageChangeAsync(value);
    }

    private async Task HandleLanguageChangeAsync(LanguageOption value)
    {
        if (IsLanguageBusy)
        {
            return;
        }

        IsLanguageBusy = true;
        try
        {
            // 先安全保存设置与语言，再询问重启。
            await SaveAsync();

            bool restart = _confirmRestartAsync is not null
                ? await _confirmRestartAsync()
                : true;
            if (restart)
            {
                bool restarted = _requestRestart();
                if (!restarted)
                {
                    StatusText = "无法自动重启，请手动退出后再启动应用。";
                    HasStatus = true;
                }
            }
        }
        catch (Exception ex)
        {
            _log?.Error($"语言切换失败: {ex.GetType().Name}");
            StatusText = "语言切换失败，请稍后重试。";
            HasStatus = true;
        }
        finally
        {
            IsLanguageBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var settings = new AppearanceSettingsData
            {
                Theme = SelectedTheme?.Theme.ToString() ?? "System",
                Language = SelectedLanguage?.Language.ToString() ?? "System",
            };
            await _store.SaveAsync(settings, CancellationToken.None);
            // 重启前安全保存：语言偏好由 LanguageService 写 PrimaryLanguageOverride，
            // 这里同时落盘以便重启后读取。
            if (SelectedLanguage is not null)
            {
                _language.ApplyLanguage(SelectedLanguage.Language);
            }
        }
        catch (Exception ex)
        {
            _log?.Error($"保存外观设置失败: {ex}");
        }
    }

    /// <summary>应用退出时调用（无在途异步操作需要取消）。</summary>
    public void Shutdown()
    {
    }
}
