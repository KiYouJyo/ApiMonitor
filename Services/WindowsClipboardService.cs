using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace ApiMonitor.Services;

/// <summary>
/// Windows 剪贴板实现。密钥只存在于调用内存与系统剪贴板，
/// 不持久化、不写入日志；延迟清理仅在内容未变化时执行。
/// </summary>
public sealed class WindowsClipboardService : IClipboardService
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DelayedClipboardGuard _guard;
    private readonly AppLog? _log;

    public WindowsClipboardService(
        DispatcherQueue dispatcherQueue,
        AppLog? log = null,
        DelayedClipboardGuard? guard = null)
    {
        _dispatcherQueue = dispatcherQueue;
        _log = log;
        _guard = guard ?? new DelayedClipboardGuard();
    }

    public Task SetSensitiveTextAsync(
        string text,
        TimeSpan clearAfter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool enqueued = _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
                completion.TrySetResult();

                _ = _guard.RunAsync(
                    text,
                    clearAfter,
                    GetCurrentTextAsync,
                    ClearClipboard,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _log?.Error($"写入剪贴板失败: {ex.GetType().Name}");
                completion.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            completion.TrySetException(new InvalidOperationException(L10n.Get("Clipboard.DispatcherUnavailable")));
        }

        return completion.Task;
    }

    public Task SetPlainTextAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool enqueued = _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                _log?.Error($"写入剪贴板失败: {ex.GetType().Name}");
                completion.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            completion.TrySetException(new InvalidOperationException(L10n.Get("Clipboard.DispatcherUnavailable")));
        }

        return completion.Task;
    }

    private Task<string?> GetCurrentTextAsync()
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcherQueue.TryEnqueue(() =>
            {
                _ = ReadClipboardCoreAsync(completion);
            }))
        {
        }
        else
        {
            completion.TrySetResult(null);
        }

        return completion.Task;
    }

    private async Task ReadClipboardCoreAsync(TaskCompletionSource<string?> completion)
    {
        try
        {
            var view = Clipboard.GetContent();
            if (!view.Contains(StandardDataFormats.Text))
            {
                completion.TrySetResult(null);
                return;
            }

            completion.TrySetResult(await view.GetTextAsync());
        }
        catch (Exception ex)
        {
            _log?.Warn($"读取剪贴板失败: {ex.GetType().Name}");
            completion.TrySetResult(null);
        }
    }

    private void ClearClipboard()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                Clipboard.Clear();
            }
            catch (Exception ex)
            {
                _log?.Warn($"清空剪贴板失败: {ex.GetType().Name}");
            }
        });
    }
}
