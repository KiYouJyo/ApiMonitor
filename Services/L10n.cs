namespace ApiMonitor.Services;

/// <summary>
/// 统一本地化入口。支持多个资源图（Resources + 功能增量资源），并对常见 UI 属性
/// 后缀做一致回退。缺失资源始终显示 [Missing: key]，避免静默空白。
/// </summary>
public static class L10n
{
    private static readonly object Gate = new();
    private static Func<string, string?>[] _resolvers = Array.Empty<Func<string, string?>>();

    public static bool IsInitialized => _resolvers.Length > 0;

    /// <summary>设置主资源解析器，并清空此前解析器。</summary>
    public static void Initialize(Func<string, string?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        lock (Gate)
        {
            _resolvers = new[] { resolver };
        }
    }

    /// <summary>追加资源解析器。用于独立功能资源图，保持既有 Resources.resw 稳定。</summary>
    public static void AddResolver(Func<string, string?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        lock (Gate)
        {
            var next = new Func<string, string?>[_resolvers.Length + 1];
            Array.Copy(_resolvers, next, _resolvers.Length);
            next[^1] = resolver;
            _resolvers = next;
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _resolvers = Array.Empty<Func<string, string?>>();
        }
    }

    public static void InitializeWithMap(IReadOnlyDictionary<string, string> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        Initialize(key => map.TryGetValue(key, out var value) ? value : null);
    }

    public static string Get(string key)
    {
        string? resolved = TryResolve(key);
        return string.IsNullOrEmpty(resolved) ? $"[Missing: {key}]" : resolved;
    }

    public static string Format(string key, params object[] args)
    {
        string? resolved = TryResolve(key);
        if (string.IsNullOrEmpty(resolved))
        {
            return $"[Missing: {key}]";
        }

        try
        {
            return string.Format(resolved, args);
        }
        catch (FormatException)
        {
            return resolved;
        }
    }

    internal static IEnumerable<string> BuildCandidates(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            yield break;
        }

        yield return key;
        foreach (string suffix in new[]
        {
            ".Text", ".Content", ".Header", ".Title", ".Message", ".Description",
            ".PlaceholderText", ".ToolTip", ".AutomationName", ".OnContent", ".OffContent",
            ".PrimaryButtonText", ".SecondaryButtonText", ".CloseButtonText",
        })
        {
            yield return key + suffix;
        }
    }

    private static string? TryResolve(string key)
    {
        var resolvers = _resolvers;
        if (resolvers.Length == 0)
        {
            return null;
        }

        foreach (string candidate in BuildCandidates(key))
        {
            foreach (var resolver in resolvers)
            {
                try
                {
                    string? value = resolver(candidate);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
                catch
                {
                    // 单一资源图异常不阻断其它资源图与后缀回退。
                }
            }
        }

        return null;
    }
}
