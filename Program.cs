using ApiMonitor.Services;
using Microsoft.UI.Dispatching;

namespace ApiMonitor;

/// <summary>
/// 自定义入口（DISABLE_XAML_GENERATED_MAIN）：
/// 在创建 XAML Application 与窗口之前完成通知处理器注册与单实例检查，
/// 保证第二进程不短暂显示窗口、不创建第二个托盘图标、不启动第二套调度。
/// 保留 STA 线程模型并正确初始化 COM/WinRT。
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // 1. 通知处理器注册必须在读取当前激活参数之前完成：
        //    先绑定 NotificationInvoked，再调用 Register。
        var notificationService = new AppNotificationService();
        notificationService.Register();

        var singleInstance = new SingleInstanceService();
        if (singleInstance.RedirectIfDuplicate())
        {
            // 第二个实例：把激活参数重定向给主实例后干净退出（含通知注销）。
            notificationService.Unregister();
            return 0;
        }

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App(singleInstance, notificationService);
        });

        return 0;
    }
}
