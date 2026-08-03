using Windows.ApplicationModel.Resources;

namespace ApiMonitor.Services;

public sealed class AppStrings : IAppStrings
{
    private readonly ResourceLoader _loader;

    public AppStrings()
    {
        // GetForViewIndependentUse 在打包与未打包（无 Package Identity）下均可用，
        // 不依赖当前视图；GetForCurrentView 在未打包调试时会抛 0x80073B27。
        _loader = ResourceLoader.GetForViewIndependentUse("Resources");
    }

    public string Get(string key)
    {
        try
        {
            // MRT Core 资源标识符以 / 分隔层级；resw 键名中的点号在 PRI 生成时
            // 被 convertDotsToSlashes 转成斜杠。这里把点号规范化为斜杠再查找，
            // 否则点号键（如 "Nav.Home.Content"）会查不到（返回空）。
            string normalized = key.Replace('.', '/');
            string value = _loader.GetString(normalized);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch
        {
            return key;
        }
    }

    public string Format(string key, params object[] args)
    {
        try
        {
            string normalized = key.Replace('.', '/');
            string value = _loader.GetString(normalized);
            if (string.IsNullOrEmpty(value))
            {
                return key;
            }

            return string.Format(value, args);
        }
        catch
        {
            return key;
        }
    }
}
