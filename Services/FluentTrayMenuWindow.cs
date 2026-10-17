using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace ApiMonitor.Services;

/// <summary>
/// Native WinUI 3 flyout window for the notification-area menu. The Shell icon
/// stays hosted by TrayNativeHost (so login startup and Explorer recovery keep
/// working), but no Win32 HMENU is shown on the normal interactive path.
/// </summary>
internal sealed class FluentTrayMenuWindow : Window
{
    private const int MenuWidth = 304;
    private readonly Grid _root;
    private readonly Border _surface;
    private readonly StackPanel _items;
    private Action<uint>? _commandSelected;
    private bool _shown;

    public FluentTrayMenuWindow()
    {
        SystemBackdrop = new DesktopAcrylicBackdrop();
        _items = new StackPanel { Spacing = 2 };
        _surface = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = _items,
            Margin = new Thickness(6),
        };
        _root = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { _surface },
            IsTabStop = true,
        };
        Content = _root;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        // Hide from the taskbar and Alt+Tab. Do not alter the dedicated Shell message window.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int ex = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE,
            (ex | (int)NativeMethods.WS_EX_TOOLWINDOW) & ~(int)NativeMethods.WS_EX_APPWINDOW);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE);

        Activated += (_, args) =>
        {
            if (_shown && args.WindowActivationState == WindowActivationState.Deactivated)
                HideMenu();
        };
        _root.KeyDown += OnKeyDown;
    }

    public bool ShowMenu(
        IReadOnlyList<TrayMenuItem> entries,
        TrayContextMenuRequest request,
        TrayNativeHost host,
        Action<uint> commandSelected,
        AppThemePreference theme)
    {
        var placement = host.ResolvePlacement(request);
        if (placement is null) return false;

        _shown = false;
        AppWindow.Hide();
        _commandSelected = commandSelected;
        _root.RequestedTheme = theme switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        bool dark = _root.ActualTheme == ElementTheme.Dark;
        _surface.Background = new SolidColorBrush(dark
            ? Color.FromArgb(238, 35, 45, 54)
            : Color.FromArgb(244, 250, 252, 254));
        _surface.BorderBrush = new SolidColorBrush(dark
            ? Color.FromArgb(170, 74, 88, 101)
            : Color.FromArgb(190, 205, 216, 224));
        _items.Children.Clear();

        int heightDips = 28;
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                _items.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(12, 6, 12, 6),
                    Background = new SolidColorBrush(dark
                        ? Color.FromArgb(130, 92, 107, 118)
                        : Color.FromArgb(170, 217, 226, 233)),
                });
                heightDips += 13;
                continue;
            }

            var label = new TextBlock
            {
                Text = entry.Text ?? string.Empty,
                FontSize = entry.CommandId == 0 ? 12 : 13,
                FontWeight = entry.IsDefault
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = new SolidColorBrush(dark
                    ? Microsoft.UI.Colors.White
                    : Color.FromArgb(255, 27, 38, 47)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(22) },
                                                          new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } } };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            if (entry.IsChecked)
            {
                var tick = new SymbolIcon(Symbol.Accept)
                {
                    Width = 16, Height = 16,
                    Foreground = new SolidColorBrush(dark
                        ? Color.FromArgb(255, 91, 193, 225)
                        : Color.FromArgb(255, 0, 105, 155)),
                };
                row.Children.Add(tick);
            }

            var button = new Button
            {
                Content = row,
                Height = entry.CommandId == 0 ? 30 : 40,
                CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Padding = new Thickness(10, 2, 10, 2),
                IsEnabled = entry.IsEnabled && entry.CommandId != 0,
            };
            if (entry.CommandId != 0)
            {
                uint id = entry.CommandId;
                button.Click += (_, _) =>
                {
                    HideMenu();
                    _commandSelected?.Invoke(id);
                };
            }
            _items.Children.Add(button);
            heightDips += (int)button.Height + 2;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        double scale = dpi > 0 ? dpi / 96d : 1d;
        int width = (int)Math.Ceiling(MenuWidth * scale);
        int height = (int)Math.Ceiling((heightDips + 8) * scale);
        var anchor = placement.Value;
        bool toLeft = (anchor.AlignFlags & NativeMethods.TPM_RIGHTALIGN) != 0;
        bool above = (anchor.AlignFlags & NativeMethods.TPM_BOTTOMALIGN) != 0;
        int left = anchor.X - (toLeft ? width : 0);
        int top = anchor.Y - (above ? height : 0);

        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.POINT { X = anchor.X, Y = anchor.Y },
            NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            left = Math.Clamp(left, info.rcWork.Left,
                Math.Max(info.rcWork.Left, info.rcWork.Right - width));
            top = Math.Clamp(top, info.rcWork.Top,
                Math.Max(info.rcWork.Top, info.rcWork.Bottom - height));
        }

        AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
        _shown = true;
        Activate();
        _root.Focus(FocusState.Programmatic);
        return true;
    }

    public void HideMenu()
    {
        if (!_shown) return;
        _shown = false;
        AppWindow.Hide();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideMenu();
            e.Handled = true;
        }
    }
}
