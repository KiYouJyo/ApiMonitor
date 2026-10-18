using System.Runtime.InteropServices;
using ApiMonitor.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ApiMonitor.Views;

/// <summary>
/// UrbanPlanToolbox-style compact WinUI 3 context menu for the existing native
/// notification-area icon. Shell callbacks and monitoring lifetime are unchanged.
/// </summary>
public sealed partial class TrayMenuWindow : Window
{
    private const double MenuWidthDip = 244;
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpShowWindow = 0x0040;

    private bool _allowClose;
    private bool _isVisible;
    private bool _hasActivatedSinceShow;
    private Action<uint>? _dispatch;

    public TrayMenuWindow()
    {
        InitializeComponent();
        AppWindow.IsShownInSwitchers = false;
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.Closing += OnClosing;
        Activated += OnActivated;
    }

    public bool ShowMenu(IReadOnlyList<TrayMenuItem> entries,
        TrayContextMenuRequest request, TrayNativeHost host,
        Action<uint> dispatch, AppThemePreference theme)
    {
        var placement = host.ResolvePlacement(request);
        if (placement is null) return false;

        _dispatch = dispatch;
        RootLayout.RequestedTheme = theme switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        ItemsHost.Children.Clear();
        double heightDip = 8; // root border + padding
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                ItemsHost.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(8, 4, 8, 4),
                    Style = (Style)RootLayout.Resources["TraySeparatorStyle"],
                });
                heightDip += 9;
                continue;
            }

            var content = new Grid { ColumnSpacing = 6 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = entry.Text ?? string.Empty,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = entry.CommandId == 0 ? 12 : 14,
                Opacity = entry.CommandId == 0 ? 0.7 : 1,
            };
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            if (entry.IsChecked)
            {
                content.Children.Add(new SymbolIcon(Symbol.Accept)
                {
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            var button = new Button
            {
                Content = content,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 0, 8, 0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                IsEnabled = entry.IsEnabled && entry.CommandId != 0,
            };
            AutomationProperties.SetName(button, entry.Text ?? string.Empty);
            if (entry.CommandId != 0)
            {
                var id = entry.CommandId;
                button.Click += (_, _) =>
                {
                    HideMenu();
                    _dispatch?.Invoke(id);
                };
            }
            ItemsHost.Children.Add(button);
            heightDip += 32;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1d : dpi / 96d;
        var size = new SizeInt32(
            Math.Max(1, (int)Math.Ceiling(MenuWidthDip * scale)),
            Math.Max(1, (int)Math.Ceiling(heightDip * scale)));

        var anchor = placement.Value;
        bool left = (anchor.AlignFlags & NativeMethods.TPM_RIGHTALIGN) != 0;
        bool above = (anchor.AlignFlags & NativeMethods.TPM_BOTTOMALIGN) != 0;
        var initial = new PointInt32(
            anchor.X - (left ? size.Width : 0),
            anchor.Y - (above ? size.Height : 0));
        var area = DisplayArea.GetFromPoint(new PointInt32(anchor.X, anchor.Y), DisplayAreaFallback.Primary).WorkArea;
        var position = new PointInt32(
            Math.Clamp(initial.X, area.X, Math.Max(area.X, area.X + area.Width - size.Width)),
            Math.Clamp(initial.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height)));

        AppWindow.Resize(size);
        AppWindow.Move(position);
        _hasActivatedSinceShow = false;
        _isVisible = true;
        AppWindow.Show();
        // UrbanPlanToolbox: promote the real context-menu HWND, including when
        // Explorer's hidden-icons popup owns the foreground.
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        Activate();
        SetForegroundWindow(hwnd);
        BringWindowToTop(hwnd);
        return true;
    }

    public void HideMenu()
    {
        if (!_isVisible) return;
        AppWindow.Hide();
        _isVisible = false;
        _hasActivatedSinceShow = false;
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        HideMenu();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (!_isVisible) return;
        if (e.WindowActivationState != WindowActivationState.Deactivated)
        {
            _hasActivatedSinceShow = true;
            return;
        }
        if (_hasActivatedSinceShow) HideMenu();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        HideMenu();
        e.Handled = true;
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hwnd);
}
