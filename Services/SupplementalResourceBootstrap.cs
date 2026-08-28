namespace ApiMonitor.Services;

/// <summary>注册 v1.1+ 增量本地化资源图，避免继续膨胀历史 Resources.resw。</summary>
public static class SupplementalResourceBootstrap
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        L10n.AddResolver(key =>
        {
            try
            {
                var loader = Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse("Modern");
                string value = loader.GetString(key.Replace('.', '/'));
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch
            {
                return null;
            }
        });
    }
}
