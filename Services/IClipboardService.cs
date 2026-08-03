namespace ApiMonitor.Services;

/// <summary>
/// 敏感文本剪贴板服务抽象。写入后可安排延迟清理；
/// 实现不得持久化传入的文本。
/// </summary>
public interface IClipboardService
{
    Task SetSensitiveTextAsync(
        string text,
        TimeSpan clearAfter,
        CancellationToken cancellationToken);

    /// <summary>
    /// 非敏感模式：复制普通文本（如诊断信息），不安排自动清理。
    /// 调用方必须保证内容非敏感。
    /// </summary>
    Task SetPlainTextAsync(string text, CancellationToken cancellationToken);
}
