using Microsoft.Windows.AppLifecycle;

namespace ApiMonitor.Services;

/// <summary>应用激活类型（当前进程初始激活或后续激活）。</summary>
public enum AppActivationKind2
{
    Launch,

    StartupTask,

    Other,
}

/// <summary>
/// 全应用单实例服务：基于 Windows App SDK 的 AppInstance。
/// 第二个进程在创建 XAML Application/窗口之前重定向激活并干净退出，
/// 不创建第二个托盘图标、不启动第二套调度、不访问 Credential Locker。
/// </summary>
public interface ISingleInstanceService
{
    /// <summary>当前进程是否为主实例（第一个启动的进程）。</summary>
    bool IsMainInstance { get; }

    /// <summary>
    /// 当前进程的初始激活类型（仅主实例调用；调用一次后缓存）。
    /// 用于区分普通启动（显示主窗口）与 StartupTask 登录启动（仅驻留托盘）。
    /// </summary>
    AppActivationKind2 GetInitialActivationKind();

    /// <summary>
    /// 第二进程调用：把激活参数重定向给主实例，返回 true 表示应退出当前进程。
    /// </summary>
    bool RedirectIfDuplicate();

    /// <summary>主实例收到后续激活时触发（普通启动重定向、登录启动重定向等）。</summary>
    event Action<AppActivationKind2>? Activated;

    /// <summary>主实例订阅激活事件（在初始化完成前调用）。</summary>
    void SubscribeActivationEvents();
}

/// <summary>基于 Microsoft.Windows.AppLifecycle.AppInstance 的实现。</summary>
public sealed class SingleInstanceService : ISingleInstanceService
{
    /// <summary>固定实例键：所有启动路径共用，防止同应用多实例。</summary>
    public const string InstanceKey = "ApiMonitor.MainInstance";

    private readonly AppInstance _mainInstance;
    private AppActivationKind2? _cachedInitialKind;

    public SingleInstanceService()
    {
        _mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
    }

    public bool IsMainInstance => _mainInstance.IsCurrent;

    public event Action<AppActivationKind2>? Activated;

    public AppActivationKind2 GetInitialActivationKind()
    {
        if (_cachedInitialKind is { } kind)
        {
            return kind;
        }

        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        var result = ToKind(args);
        _cachedInitialKind = result;
        return result;
    }

    public bool RedirectIfDuplicate()
    {
        if (IsMainInstance)
        {
            return false;
        }

        try
        {
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            _mainInstance.RedirectActivationToAsync(args).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // 重定向失败不阻塞退出：第二进程仍应干净结束。
        }

        return true;
    }

    public void SubscribeActivationEvents()
    {
        AppInstance.GetCurrent().Activated += OnActivated;
    }

    private void OnActivated(object? sender, AppActivationArguments args)
    {
        Activated?.Invoke(ToKind(args));
    }

    private static AppActivationKind2 ToKind(AppActivationArguments? args)
    {
        if (args is null)
        {
            return AppActivationKind2.Launch;
        }

        return args.Kind switch
        {
            ExtendedActivationKind.Launch => AppActivationKind2.Launch,
            ExtendedActivationKind.StartupTask => AppActivationKind2.StartupTask,
            _ => AppActivationKind2.Other,
        };
    }
}
