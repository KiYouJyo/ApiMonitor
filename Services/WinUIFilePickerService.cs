using Windows.Storage;
using Windows.Storage.Pickers;

namespace ApiMonitor.Services;

/// <summary>
/// WinUI 3 文件选择器实现（基于 Windows.Storage.Pickers）。
/// 必须在 UI 线程使用；WinUI 3 桌面应用需要初始化窗口句柄。
/// </summary>
public sealed class WinUIFilePickerService : IFilePickerService
{
    private readonly Func<IntPtr> _windowHandleProvider;
    private readonly nint _hwnd;

    public WinUIFilePickerService(Func<IntPtr> windowHandleProvider)
    {
        _windowHandleProvider = windowHandleProvider;
        _hwnd = IntPtr.Zero;
    }

    private static void InitializeForWindow(FileOpenPicker picker, nint hwnd) =>
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

    private static void InitializeForWindow(FileSavePicker picker, nint hwnd) =>
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

    public async Task<string?> PickSaveFileAsync(
        string suggestedFileName,
        IReadOnlyList<string> extensions,
        CancellationToken cancellationToken)
    {
        try
        {
            nint hwnd = _windowHandleProvider();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            var picker = new FileSavePicker
            {
                SuggestedFileName = suggestedFileName,
            };
            foreach (var ext in extensions)
            {
                picker.FileTypeChoices.Add(ext.TrimStart('.').ToUpperInvariant(), new List<string> { ext });
            }

            InitializeForWindow(picker, hwnd);
            StorageFile? file = await picker.PickSaveFileAsync().AsTask(cancellationToken);
            return file?.Path;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> PickOpenFileAsync(
        IReadOnlyList<string> extensions,
        CancellationToken cancellationToken)
    {
        try
        {
            nint hwnd = _windowHandleProvider();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            var picker = new FileOpenPicker();
            foreach (var ext in extensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            InitializeForWindow(picker, hwnd);
            StorageFile? file = await picker.PickSingleFileAsync().AsTask(cancellationToken);
            return file?.Path;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}
