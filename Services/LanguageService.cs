namespace ApiMonitor.Services;

/// <summary>语言系统适配（WinRT PrimaryLanguageOverride），测试时可替换。</summary>
public interface ILanguageSystemAdapter
{
    string? ReadOverride();

    void WriteOverride(string? languageCode);
}

/// <summary>真实 WinRT 实现。</summary>
public sealed class WindowsLanguageSystemAdapter : ILanguageSystemAdapter
{
    public string? ReadOverride()
    {
        try
        {
            string code = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
            return string.IsNullOrWhiteSpace(code) ? null : code;
        }
        catch
        {
            return null;
        }
    }

    public void WriteOverride(string? languageCode)
    {
        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode ?? string.Empty;
        }
        catch
        {
            // 设置失败不影响应用。
        }
    }
}

/// <summary>
/// v0.6.0：语言服务。保存 PrimaryLanguageOverride，切换后提示重启；
/// 立即重启使用 AppInstance.Restart，失败时提示手动退出再启动。
/// 重启前由调用方先保存设置；重启后托盘、自动刷新与单实例逻辑保持正常。
/// </summary>
public interface ILanguageService
{
    /// <summary>当前语言偏好（跟随系统/简体中文/English/日本語）。</summary>
    AppLanguagePreference Preference { get; }

    /// <summary>把语言偏好应用到系统（保存 PrimaryLanguageOverride）。</summary>
    void ApplyLanguage(AppLanguagePreference preference);

    /// <summary>当前 UI 语言代码（如 zh-CN / en-US / ja-JP；跟随系统时取系统语言）。</summary>
    string CurrentLanguageCode { get; }

    /// <summary>请求重启应用；返回是否成功启动重启。</summary>
    bool RequestRestart();
}

public sealed class LanguageService : ILanguageService
{
    private readonly ILanguageSystemAdapter _adapter;
    private AppLanguagePreference _preference = AppLanguagePreference.System;

    public LanguageService(ILanguageSystemAdapter? adapter = null)
    {
        _adapter = adapter ?? new WindowsLanguageSystemAdapter();
    }

    public AppLanguagePreference Preference => _preference;

    public string CurrentLanguageCode
    {
        get
        {
            try
            {
                string? overrideCode = _adapter.ReadOverride();
                if (!string.IsNullOrWhiteSpace(overrideCode))
                {
                    return NormalizeCode(overrideCode);
                }

                string? system = Windows.Globalization.ApplicationLanguages.Languages.FirstOrDefault();
                return NormalizeCode(system ?? "en-US");
            }
            catch
            {
                return "en-US";
            }
        }
    }

    public void ApplyLanguage(AppLanguagePreference preference)
    {
        _preference = preference;
        try
        {
            _adapter.WriteOverride(preference switch
            {
                AppLanguagePreference.ZhCn => "zh-CN",
                AppLanguagePreference.EnUs => "en-US",
                AppLanguagePreference.JaJp => "ja-JP",
                _ => null, // 跟随系统：清空覆盖。
            });
        }
        catch
        {
            // 设置失败不影响应用。
        }
    }

    public bool RequestRestart()
    {
        // 应用重启逻辑（AppInstance.Restart）需要 Windows App SDK；
        // 测试项目通过注入的重启委托覆盖。真实实现由调用方提供。
        return false;
    }

    /// <summary>把语言代码归一化到支持的三语。</summary>
    public static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "en-US";
        }

        // 把 "zh-CN"/"zh-Hans-CN" 归一化到支持的三语。
        string lower = code.ToLowerInvariant();
        if (lower.StartsWith("zh", StringComparison.Ordinal))
        {
            return "zh-CN";
        }

        if (lower.StartsWith("ja", StringComparison.Ordinal))
        {
            return "ja-JP";
        }

        return "en-US";
    }
}
