using Windows.ApplicationModel.Resources;

namespace ApiMonitor.Services;

public sealed class AppStrings : IAppStrings
{
    private readonly ResourceLoader _loader;

    public AppStrings()
    {
        _loader = ResourceLoader.GetForCurrentView("Resources");
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
