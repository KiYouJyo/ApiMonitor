using ApiMonitor.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace ApiMonitor.Services;

/// <summary>
/// 包装 WinUI Window 的悬浮余额窗宿主：负责激活/恢复、账户切换与关闭事件的转发。
/// </summary>
public sealed class WinUIFloatingWindowHost : IFloatingWindowHost
{
    private readonly FloatingBalanceWindow _window;
    private bool _isOpen;

    public WinUIFloatingWindowHost(FloatingBalanceWindow window)
    {
        _window = window;
        _window.Closed += OnWindowClosed;
    }

    public bool IsOpen => _isOpen;

    public event EventHandler? Closed;

    public void ShowOrActivate(string? accountId = null)
    {
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            _window.SelectAccount(accountId);
        }

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
