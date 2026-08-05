using ApiMonitor.Models;
using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

public sealed partial class AccountEditorDialog : ContentDialog
{
    public AccountEditorViewModel ViewModel { get; }

    public AccountEditorResult? Result { get; private set; }

    /// <summary>对话框按钮/开关文本（v0.6.0 本地化，供 x:Bind）。</summary>
    public string SaveButtonText => Services.L10n.Get("Common.Save");

    public string CancelButtonText => Services.L10n.Get("Common.Cancel");

    public string OnContentText => Services.L10n.Get("Settings.On");

    public string OffContentText => Services.L10n.Get("Settings.Off");

    public string AccountNamePlaceholderText => Services.L10n.Get("Dialog.AccountNamePlaceholder");

    public string ApiKeyHeaderText => Services.L10n.Get("Dialog.ApiKey.Header");

    public string TestConnectionText => Services.L10n.Get("Dialog.TestConnection.Content");

    public AccountEditorDialog(AccountEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        // 切换 Provider 时清空密码框，避免残留上一供应商的敏感输入。
        ViewModel.ApiKeyCleared += ClearApiKey;

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

    private void ClearApiKey()
    {
        if (ApiKeyPasswordBox is not null)
        {
            ApiKeyPasswordBox.Password = string.Empty;
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
