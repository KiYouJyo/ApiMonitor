using Windows.Storage;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 解析应用专属本地数据目录。打包运行时使用 ApplicationData.LocalFolder，
/// 失败时回退到 %LOCALAPPDATA%\ApiBalanceMonitor，保证普通权限可用。
/// </summary>
public static class AppPaths
{
    public static string GetLocalDataDirectory()
    {
        try
        {
            return ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ApiBalanceMonitor");
        }
    }
}
