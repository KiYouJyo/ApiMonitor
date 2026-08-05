using ApiMonitor.Models;
using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml.Media;
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

    private void OnCredentialSlotPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            if (box.DataContext is CredentialSlotItem item)
            {
                ViewModel.SetCredentialSlotValue(item.SlotId, box.Password);
            }
        }
    }

    private void ClearApiKey()
    {
        foreach (var box in FindDescendants<PasswordBox>(this))
        {
            box.Password = string.Empty;
        }
    }

    /// <summary>遍历可视树查找指定类型后代（用于清空模板内密码框）。</summary>
    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
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
