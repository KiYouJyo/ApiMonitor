namespace ApiMonitor.Services;

/// <summary>
/// 请求目标的日志/异常安全表示（v0.9.0）。
/// 日志与异常绝不允许包含完整查询字符串：key/ak/tk/sig/sn/token 等
/// 敏感参数会随查询串泄漏。因此一律只输出 scheme://host/path。
/// </summary>
public static class QuerySanitizer
{
    /// <summary>
    /// 返回安全的请求目标文本：scheme://host/path，不含查询字符串、
    /// 不含用户名/密码，也不含 fragment。
    /// </summary>
    public static string SafeRequestTarget(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return "<invalid-uri>";
        }

        string path = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        return $"{uri.Scheme}://{uri.Host}{path}";
    }

    /// <summary>
    /// 移除查询字符串中的敏感参数（key/ak/tk/sig/sn/token/authorization/password/sk）。
    /// 供日志使用；异常信息仍应优先使用 <see cref="SafeRequestTarget"/>。
    /// </summary>
    public static string SanitizeQuery(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query))
        {
            return string.Empty;
        }

        var pairs = new List<string>();
        foreach (string part in uri.Query.TrimStart('?').Split('&'))
        {
            int eq = part.IndexOf('=');
            string name = eq < 0 ? part : part[..eq];
            if (IsSensitiveParameter(name))
            {
                continue;
            }

            pairs.Add(part);
        }

        return pairs.Count == 0 ? string.Empty : "?" + string.Join("&", pairs);
    }

    public static bool IsSensitiveParameter(string name) =>
        name.Trim().ToLowerInvariant() is "key" or "ak" or "tk" or "sig" or "sn"
            or "token" or "authorization" or "password" or "sk" or "apikey" or "api_key";
}
