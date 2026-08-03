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
}
