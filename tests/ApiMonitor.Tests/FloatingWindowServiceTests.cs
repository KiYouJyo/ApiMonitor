using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

/// <summary>悬浮窗单实例协调测试（使用假宿主，不弹出真实窗口）。</summary>
public sealed class FloatingWindowServiceTests
{
    private sealed class FakeHost : IFloatingWindowHost
    {
        public bool IsOpen { get; private set; }

        public int ShowCalls { get; private set; }

        public List<string?> ShownAccountIds { get; } = new();

        public event EventHandler? Closed;

        public void ShowOrActivate(string? accountId = null)
        {
            ShowCalls++;
            ShownAccountIds.Add(accountId);
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
        var service = new FloatingWindowService(() =>
        {
            var host = new FakeHost();
            hosts.Add(host);
            return host;
        });

        service.Show();
        service.Show();

        Assert.Single(hosts);
        Assert.Equal(2, hosts[0].ShowCalls);
        Assert.True(service.IsWindowOpen);
    }

    [Fact]
    public void ShowWithAccountId_ForwardsToHost()
    {
        var hosts = new List<FakeHost>();
        var service = new FloatingWindowService(() =>
        {
            var host = new FakeHost();
            hosts.Add(host);
            return host;
        });

        service.Show("acct-a");
        service.Show("acct-b");

        Assert.Single(hosts);
        Assert.Equal(new string?[] { "acct-a", "acct-b" }, hosts[0].ShownAccountIds);
    }

    [Fact]
    public void AfterClose_CanOpenAgainWithNewHost()
    {
        var hosts = new List<FakeHost>();
        var service = new FloatingWindowService(() =>
        {
            var host = new FakeHost();
            hosts.Add(host);
            return host;
        });

        service.Show();
        service.CloseWindow();
        Assert.False(service.IsWindowOpen);

        service.Show();

        Assert.Equal(2, hosts.Count);
        Assert.True(service.IsWindowOpen);
    }

    [Fact]
    public void HostSelfClosed_AllowsRecreate()
    {
        var hosts = new List<FakeHost>();
        var service = new FloatingWindowService(() =>
        {
            var host = new FakeHost();
            hosts.Add(host);
            return host;
        });

        service.Show();
        hosts[0].Close();

        Assert.False(service.IsWindowOpen);

        service.Show();
        Assert.Equal(2, hosts.Count);
    }

    [Fact]
    public void Shutdown_ClosesHostAndClearsReference()
    {
        var hosts = new List<FakeHost>();
        var service = new FloatingWindowService(() =>
        {
            var host = new FakeHost();
            hosts.Add(host);
            return host;
        });

        service.Show();
        service.Shutdown();

        Assert.False(hosts[0].IsOpen);
        Assert.False(service.IsWindowOpen);
    }
}
