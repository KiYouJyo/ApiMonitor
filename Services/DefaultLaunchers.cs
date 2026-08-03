using ApiMonitor.Services;

namespace ApiMonitor.Services;

/// <summary>
/// 外部链接启动器实现（Launcher.LaunchUriAsync），失败返回 false 不抛异常。
/// </summary>
public sealed class DefaultExternalLinkLauncher : IExternalLinkLauncher
{
    public async Task<bool> LaunchUriAsync(Uri uri)
    {
        try
        {
            return await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 打开 Package LocalState 数据文件夹。打包运行时用 ApplicationData.LocalFolder
/// 直接暴露（explorer 打开）；未打包时打开 %LOCALAPPDATA%\ApiMonitor。
/// 不打开 Credential Locker 或证书管理器。
/// </summary>
public sealed class LocalDataFolderOpener : ILocalDataFolderOpener
{
    public Task<bool> OpenAsync()
    {
        try
        {
            string path = AppPaths.GetLocalDataDirectory();
            Directory.CreateDirectory(path);
            return Task.FromResult(Windows.System.Launcher.LaunchFolderPathAsync(path).AsTask().Result);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
