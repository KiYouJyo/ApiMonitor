using ApiBalanceMonitor.Services;

namespace ApiBalanceMonitor.Tests.TestDoubles;

public sealed class FakeUiThreadInvoker : IUiThreadInvoker
{
    public void Post(Action action) => action();
}
