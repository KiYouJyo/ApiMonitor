using ApiMonitor.Services;

namespace ApiMonitor.Tests.TestDoubles;

public sealed class FakeUiThreadInvoker : IUiThreadInvoker
{
    public void Post(Action action) => action();
}
