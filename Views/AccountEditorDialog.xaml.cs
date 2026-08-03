using ApiMonitor.Models;
using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

public sealed partial class AccountEditorDialog : ContentDialog
{
    public AccountEditorViewModel ViewModel { get; }

    public AccountEditorResult? Result { get; private set; }

    public AccountEditorDialog(AccountEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        // 对话框自身的 Resources 需在 InitializeComponent 之后才可用，
        // 因此局部圆角样式在此应用，而不是在 XAML 根元素上引用。
        if (Resources.TryGetValue("AccountEditorDialogStyle", out var style) && style is Style dialogStyle)
        {
            Style = dialogStyle;
        }
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
