using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>紧凑窗口单实例协调测试（使用假宿主，不弹出真实窗口）。</summary>
public sealed class CompactWindowServiceTests
{
    private sealed class FakeHost : ICompactWindowHost
    {
        public bool IsOpen { get; private set; }

        public int ShowCalls { get; private set; }

        public event EventHandler? Closed;

        public void ShowOrActivate()
        {
            ShowCalls++;
            IsOpen = true;
        }

        public void Close()
        {
            if (IsOpen)
            {
                IsOpen = false;
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    [Fact]
    public void OpenTwice_CreatesOnlyOneHostAndActivates()
    {
        var hosts = new List<FakeHost>();
        var service = new CompactWindowService(() =>
        {
            var host = new FakeHost();
            hosts.Add(host);
            return host;
        });

        service.OpenOrActivate();
        service.OpenOrActivate();

        Assert.Single(hosts);
        Assert.Equal(2, hosts[0].ShowCalls);
        Assert.True(service.IsWindowOpen);
    }

    [Fact]
    public void AfterClose_CanOpenAgainWithNewHost()
    {
        var hosts = new List<FakeHost>();
        var service = new CompactWindowService(() =>
        {
            var host = new FakeHost();
            hosts.Add(host);
            return host;
        });

        service.OpenOrActivate();
        service.CloseWindow();
        Assert.False(service.IsWindowOpen);

        service.OpenOrActivate();

        Assert.Equal(2, hosts.Count);
        Assert.True(service.IsWindowOpen);
    }

    [Fact]
    public void HostSelfClosed_AllowsRecreate()
    {
        var hosts = new List<FakeHost>();
        var service = new CompactWindowService(() =>
        {
            var host = new FakeHost();
            hosts.Add(host);
            return host;
        });

        service.OpenOrActivate();
        hosts[0].Close();

        Assert.False(service.IsWindowOpen);

        service.OpenOrActivate();
        Assert.Equal(2, hosts.Count);
    }

    [Fact]
    public void Shutdown_ClosesHostAndClearsReference()
    {
        var hosts = new List<FakeHost>();
        var service = new CompactWindowService(() =>
        {
            var host = new FakeHost();
            hosts.Add(host);
            return host;
        });

        service.OpenOrActivate();
        service.Shutdown();

        Assert.False(hosts[0].IsOpen);
        Assert.False(service.IsWindowOpen);
    }
}
