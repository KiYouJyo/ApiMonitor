using ApiMonitor.Services;
using Microsoft.UI.Xaml;

namespace ApiMonitor;

public sealed partial class MainWindow : Window
{
    public MainWindow(CompositionRoot compositionRoot)
    {
        InitializeComponent();
        Title = "ApiMonitor";
    }

    /// <summary>页面根元素（x:Name 字段为私有，通过此属性公开给 App）。</summary>
    public Views.MainPage RootPage => RootPageControl;
}
