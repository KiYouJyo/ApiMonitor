namespace ApiMonitor.Services;

/// <summary>紧凑窗口的最小宿主抽象（真实实现包装 WinUI Window），便于测试单实例逻辑。</summary>
public interface ICompactWindowHost
{
    bool IsOpen { get; }

    event EventHandler? Closed;

    void ShowOrActivate();

    void Close();
}
