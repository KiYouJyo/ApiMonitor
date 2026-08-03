using ApiBalanceMonitor.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ApiBalanceMonitor.Views;

public sealed partial class BalanceHistoryDialog : ContentDialog
{
    public BalanceHistoryDialog(BalanceHistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
