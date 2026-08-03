namespace ApiMonitor.Services;

/// <summary>
/// 单实例紧凑窗口协调器：首次打开创建宿主；已存在时激活而非重建；
/// 关闭后清除引用，允许再次打开。
/// </summary>
public sealed class CompactWindowService : ICompactWindowService
{
    private readonly Func<ICompactWindowHost> _hostFactory;
    private ICompactWindowHost? _host;

    public CompactWindowService(Func<ICompactWindowHost> hostFactory)
    {
        _hostFactory = hostFactory;
    }

    public bool IsWindowOpen => _host is { IsOpen: true };

    public void OpenOrActivate()
    {
        if (_host is null)
        {
            _host = _hostFactory();
            _host.Closed += OnHostClosed;
        }

        _host.ShowOrActivate();
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
