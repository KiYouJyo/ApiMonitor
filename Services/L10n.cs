namespace ApiMonitor.Services;

/// <summary>
/// v0.6.0 静态本地化入口：VM/服务直接 L10n.Get("Key") 取当前语言文本。
/// 无 WinRT 依赖（测试项目可链接）：解析器由 App 启动时通过
/// Initialize(Func) 注入（ResourceLoader 斜杠键）；未初始化时返回
/// "[Missing: 键名]"，绝不静默返回空字符串。
/// </summary>
public static class L10n
{
    private static Func<string, string?>? _resolver;
    private static volatile bool _initialized;

    /// <summary>注入解析器（App 启动时用 ResourceLoader 实现）。</summary>
    public static void Initialize(Func<string, string?> resolver)
    {
        _resolver = resolver;
        _initialized = true;
    }

    /// <summary>是否已初始化（测试可查询）。</summary>
    public static bool IsInitialized => _initialized;

    /// <summary>测试或调试用：直接指定键→值映射。</summary>
    public static void InitializeWithMap(IReadOnlyDictionary<string, string> map)
    {
        _resolver = key => map.TryGetValue(key, out var v) ? v : null;
        _initialized = true;
    }

    public static void Reset()
    {
        _resolver = null;
        _initialized = false;
    }

    public static string Get(string key)
    {
        string resolved = TryResolve(key);
        return string.IsNullOrEmpty(resolved) ? $"[Missing: {key}]" : resolved;
    }

    public static string Format(string key, params object[] args)
    {
        string resolved = TryResolve(key);
        if (string.IsNullOrEmpty(resolved))
        {
            return $"[Missing: {key}]";
        }

        try
        {
            return string.Format(resolved, args);
        }
        catch
        {
            return resolved;
        }
    }

    /// <summary>完整键或常见属性后缀（.Text/.Content/.Header）解析。</summary>
    private static string TryResolve(string key)
    {
        if (string.IsNullOrEmpty(key) || _resolver is null)
        {
            return string.Empty;
        }

        string? TryGet(string k)
        {
            try
            {
                return _resolver is null ? null : _resolver(k);
            }
            catch
            {
                return null;
            }
        }

        if (TryGet(key) is { } direct)
        {
            return direct;
        }

        foreach (string suffix in new[] { ".Text", ".Content", ".Header", ".Title", ".Message" })
        {
            if (TryGet(key + suffix) is { } withSuffix)
            {
                return withSuffix;
            }
        }

        return string.Empty;
    }
}
