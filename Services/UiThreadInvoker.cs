using Microsoft.UI.Dispatching;

namespace ApiBalanceMonitor.Services;

public sealed class UiThreadInvoker : IUiThreadInvoker
{
    private readonly DispatcherQueue _dispatcherQueue;

    public UiThreadInvoker(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public void Post(Action action) => _dispatcherQueue.TryEnqueue(() => action());
}
