namespace ApiMonitor.Services;

/// <summary>把回调投递到 UI 线程的抽象，便于 ViewModel 单元测试。</summary>
public interface IUiThreadInvoker
{
    void Post(Action action);
}
