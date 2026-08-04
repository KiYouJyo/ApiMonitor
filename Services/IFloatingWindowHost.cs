namespace ApiMonitor.Services;

/// <summary>悬浮余额窗的最小宿主抽象（真实实现包装 WinUI Window），便于测试单实例逻辑。</summary>
public interface IFloatingWindowHost
{
    bool IsOpen { get; }

    event EventHandler? Closed;

    /// <summary>显示/激活窗口；accountId 非空时先切换为该账户。</summary>
    void ShowOrActivate(string? accountId = null);

    void Close();
}
