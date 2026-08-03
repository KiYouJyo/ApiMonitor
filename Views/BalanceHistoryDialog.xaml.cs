using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

public sealed partial class BalanceHistoryDialog : ContentDialog
{
    public BalanceHistoryDialog(BalanceHistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
