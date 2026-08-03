namespace ApiMonitor.Services;

/// <summary>文件选择器结果。</summary>
public sealed record FilePickResult(string? Path);

/// <summary>
/// v0.6.0：文件选择器抽象（保存 CSV、保存备份、打开备份），
/// 视图层用 Windows Storage Picker 实现，测试层用 Fake 替换。
/// </summary>
public interface IFilePickerService
{
    /// <summary>让用户选择保存位置并返回路径；取消返回 null。</summary>
    Task<string?> PickSaveFileAsync(
        string suggestedFileName,
        IReadOnlyList<string> extensions,
        CancellationToken cancellationToken);

    /// <summary>让用户选择要打开的文件并返回路径；取消返回 null。</summary>
    Task<string?> PickOpenFileAsync(
        IReadOnlyList<string> extensions,
        CancellationToken cancellationToken);
}

/// <summary>
/// v0.6.0：外部链接启动器抽象（Launcher），测试层可替换。
/// </summary>
public interface IExternalLinkLauncher
{
    /// <summary>打开外部链接（浏览器/文件资源管理器）；返回是否成功。</summary>
    Task<bool> LaunchUriAsync(Uri uri);
}

/// <summary>
/// v0.6.0：本地数据文件夹打开器（只打开 Package LocalState，不打开
/// Credential Locker 或证书管理器）。
/// </summary>
public interface ILocalDataFolderOpener
{
    Task<bool> OpenAsync();
}
