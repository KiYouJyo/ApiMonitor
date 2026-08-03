using Windows.Storage;

namespace ApiMonitor.Services;

/// <summary>
/// 解析应用专属本地数据目录。打包运行时使用 ApplicationData.LocalFolder，
/// 失败时回退到 %LOCALAPPDATA%\ApiMonitor，保证普通权限可用。
/// 未打包模式下会自动把旧目录 %LOCALAPPDATA%\ApiBalanceMonitor
/// 一次性迁移到新目录（幂等，打包模式不移动数据）。
/// </summary>
public static class AppPaths
{
    /// <summary>未打包模式的默认数据目录名（ApiMonitor）。</summary>
    public const string UnpackagedDirectoryName = "ApiMonitor";

    /// <summary>旧版未打包数据目录名（ApiBalanceMonitor），仅用于一次性迁移。</summary>
    public const string LegacyUnpackagedDirectoryName = "ApiBalanceMonitor";

    public static string GetLocalDataDirectory()
    {
        try
        {
            return ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            string target = GetUnpackagedDataDirectory();
            UnpackagedDataMigrator.MigrateOnce(target);
            return target;
        }
    }

    internal static string GetUnpackagedDataDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UnpackagedDirectoryName);
}

/// <summary>
/// 未打包模式本地数据目录的一次性迁移：仅当新目录不存在且旧目录存在时
/// 移动旧目录到新目录；其余情况为幂等空操作。
/// </summary>
public static class UnpackagedDataMigrator
{
    public static void MigrateOnce(string targetDirectory)
    {
        if (Directory.Exists(targetDirectory))
        {
            // 新目录已存在：以新目录为准，不合并、不删除旧目录。
            return;
        }

        string? root = Path.GetDirectoryName(targetDirectory);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        string legacyDirectory = Path.Combine(root, AppPaths.LegacyUnpackagedDirectoryName);
        if (!Directory.Exists(legacyDirectory))
        {
            return;
        }

        Directory.Move(legacyDirectory, targetDirectory);
    }
}
