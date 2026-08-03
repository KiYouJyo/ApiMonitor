using ApiMonitor.Services;
using Microsoft.UI.Dispatching;

namespace ApiMonitor;

/// <summary>
/// 自定义入口（DISABLE_XAML_GENERATED_MAIN）：
/// 在创建 XAML Application 与窗口之前完成单实例检查与激活重定向，
/// 保证第二进程不短暂显示窗口、不创建第二个托盘图标。
/// 保留 STA 线程模型并正确初始化 COM/WinRT。
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var singleInstance = new SingleInstanceService();
        if (singleInstance.RedirectIfDuplicate())
        {
            // 第二个实例：重定向激活后干净退出。
            return 0;
        }

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App(singleInstance);
        });

        return 0;
    }
}
