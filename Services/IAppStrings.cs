namespace ApiMonitor.Services;

/// <summary>
/// v0.6.0：统一字符串服务接口（无 WinRT 依赖，测试项目可链接）。
/// 实现按当前 UI 语言取字符串；XAML 静态文本使用 x:Uid + 资源键。
/// </summary>
public interface IAppStrings
{
    /// <summary>按键取字符串；缺失时返回键名本身（不静默返回空）。</summary>
    string Get(string key);

    /// <summary>按键取字符串并格式化（{0}/{1}…）。</summary>
    string Format(string key, params object[] args);
}
