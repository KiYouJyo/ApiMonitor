namespace ApiMonitor.Services;

/// <summary>
/// 延迟清理协调器：等待指定时间后读取剪贴板，仅当内容未变化时清除。
/// 读取/清除通过委托注入，便于用假剪贴板做单元测试。
/// </summary>
public sealed class DelayedClipboardGuard
{
    public async Task RunAsync(
        string copiedText,
        TimeSpan delay,
        Func<Task<string?>> getCurrentTextAsync,
        Action clear,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 应用关闭/取消：不再尝试清理。
            return;
        }

        string? current;
        try
        {
            current = await getCurrentTextAsync();
        }
        catch
        {
            // 剪贴板暂时被占用等读取失败：不清空，也不崩溃。
            return;
        }

        if (!ClipboardPolicy.ShouldClear(copiedText, current))
        {
            return;
        }

        try
        {
            clear();
        }
        catch
        {
            // 清理失败不影响应用。
        }
    }
}
