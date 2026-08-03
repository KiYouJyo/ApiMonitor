using Windows.Storage;

namespace ApiMonitor.Services;

/// <summary>
/// 解析应用专属本地数据目录。打包运行时使用 ApplicationData.LocalFolder，
/// 失败时回退到 %LOCALAPPDATA%\ApiMonitor，保证普通权限可用。
/// </summary>
public static class AppPaths
{
    /// <summary>未打包模式的默认数据目录名（ApiMonitor）。</summary>
    public const string UnpackagedDirectoryName = "ApiMonitor";

    public static string GetLocalDataDirectory()
    {
        try
        {
            return ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            return GetUnpackagedDataDirectory();
        }
    }

    internal static string GetUnpackagedDataDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UnpackagedDirectoryName);
}
