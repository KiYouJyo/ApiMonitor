using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

/// <summary>
/// v1.0.0：首次启动引导页（四步）。ViewModel 由 MainViewModel 注入，
/// 页面不持有状态；键盘/Narrator 通过标准 Button/TextBlock 控件满足。
/// </summary>
public sealed partial class OnboardingPage : UserControl
{
    public OnboardingPage()
    {
        InitializeComponent();
    }

    public MainViewModel ViewModel
    {
        get => (MainViewModel)DataContext;
        set => DataContext = value;
    }
}
