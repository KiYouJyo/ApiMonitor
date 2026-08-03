using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

public sealed partial class MainPage : UserControl
{
    public MainPage()
    {
        InitializeComponent();
    }

    public MainViewModel ViewModel
    {
        get => (MainViewModel)DataContext;
        set => DataContext = value;
    }
}
