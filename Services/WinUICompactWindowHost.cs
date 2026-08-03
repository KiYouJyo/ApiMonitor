using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace ApiMonitor.Services;

/// <summary>
/// 包装 WinUI Window 的紧凑窗口宿主：负责激活/恢复与关闭事件的转发。
/// </summary>
public sealed class WinUICompactWindowHost : ICompactWindowHost
{
    private readonly Window _window;
    private bool _isOpen;

    public WinUICompactWindowHost(Window window)
    {
        _window = window;
        _window.Closed += OnWindowClosed;
    }

    public bool IsOpen => _isOpen;

    public event EventHandler? Closed;

    public void ShowOrActivate()
    {
        if (!_isOpen)
        {
            _isOpen = true;
            _window.Activate();
            return;
        }

        try
        {
            if (_window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            {
                presenter.Restore();
            }

            _window.Activate();
        }
        catch
        {
            _window.Activate();
        }
    }

    public void Close()
    {
        if (_isOpen)
        {
            _window.Close();
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isOpen)
        {
            _isOpen = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
