namespace ApiMonitor.Services;

/// <summary>
/// 单实例悬浮余额窗协调器：首次打开创建宿主；已存在时激活而非重建；
/// 关闭后清除引用，允许再次打开。
/// </summary>
public sealed class FloatingWindowService : IFloatingWindowService
{
    private readonly Func<IFloatingWindowHost> _hostFactory;
    private IFloatingWindowHost? _host;

    public FloatingWindowService(Func<IFloatingWindowHost> hostFactory)
    {
        _hostFactory = hostFactory;
    }

    public bool IsWindowOpen => _host is { IsOpen: true };

    public void Show(string? accountId = null)
    {
        if (_host is null)
        {
            _host = _hostFactory();
            _host.Closed += OnHostClosed;
        }

        _host.ShowOrActivate(accountId);
    }

    public void CloseWindow()
    {
        if (_host is not null)
        {
            _host.Close();
        }
    }

    public void Shutdown()
    {
        CloseWindow();
        if (_host is not null)
        {
            _host.Closed -= OnHostClosed;
            _host = null;
        }
    }

    private void OnHostClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _host))
        {
            return;
        }

        if (_host is not null)
        {
            _host.Closed -= OnHostClosed;
            _host = null;
        }
    }
}
