using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

/// <summary>
/// 独立设置页：集中全局设置（通知区域与启动、余额提醒、应用信息）。
/// 与主页共享同一个 MainViewModel 与设置 ViewModel，不重复创建；
/// 开关变化沿用现有即时保存逻辑。
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)DataContext;
        set => DataContext = value;
    }
}
