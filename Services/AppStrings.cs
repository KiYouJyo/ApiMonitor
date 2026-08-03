namespace ApiMonitor.Services;

/// <summary>
/// 统一字符串服务：委托 L10n（共享 ResourceContext 语言限定），
/// 保证托盘/通知等代码文本与页面 Loc 文本使用同一语言解析。
/// </summary>
public sealed class AppStrings : IAppStrings
{
    public string Get(string key)
    {
        // L10n 找不到时返回 "[Missing: 键名]"；这里保持接口语义返回键名。
        string value = L10n.Get(key);
        return value.StartsWith("[Missing:", StringComparison.Ordinal) ? key : value;
    }

    public string Format(string key, params object[] args)
    {
        string value = L10n.Format(key, args);
        return value.StartsWith("[Missing:", StringComparison.Ordinal) ? key : value;
    }
}
