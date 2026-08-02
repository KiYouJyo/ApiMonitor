using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiBalanceMonitor.Views;

public sealed partial class AccountEditorDialog : ContentDialog
{
    public AccountEditorViewModel ViewModel { get; }

    public AccountEditorResult? Result { get; private set; }

    public AccountEditorDialog(AccountEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            ViewModel.SetApiKey(box.Password);
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!ViewModel.TryBuildResult(out var result))
        {
            args.Cancel = true;
            return;
        }

        Result = result;
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
    }
}
