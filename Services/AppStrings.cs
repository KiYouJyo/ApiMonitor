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
            string value = _loader.GetString(key);
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
            string value = _loader.GetString(key);
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
