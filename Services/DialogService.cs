using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.ViewModels;
using ApiBalanceMonitor.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// WinUI 对话框实现。ContentDialog 属于 UI 层，
/// 网络与持久化仍全部经由 IAccountManager 完成。
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IAccountManager _accountManager;
    private XamlRoot? _xamlRoot;

    public DialogService(IAccountManager accountManager)
    {
        _accountManager = accountManager;
    }

    public void Attach(XamlRoot xamlRoot) => _xamlRoot = xamlRoot;

    public async Task<AccountEditorResult?> ShowAccountEditorAsync(
        AccountEditorContext context,
        CancellationToken cancellationToken)
    {
        var viewModel = new AccountEditorViewModel(_accountManager, context);
        var dialog = new AccountEditorDialog(viewModel);
        if (_xamlRoot is not null)
        {
            dialog.XamlRoot = _xamlRoot;
        }

        using var registration = cancellationToken.Register(dialog.Hide);
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? dialog.Result : null;
    }

    public async Task<bool> ConfirmDeleteAsync(string accountName, CancellationToken cancellationToken)
    {
        var dialog = new ContentDialog
        {
            Title = "删除账户",
            Content = $"确定要删除账户“{accountName}”吗？其保存的 API Key 与本地余额快照也会一并删除。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        if (_xamlRoot is not null)
        {
            dialog.XamlRoot = _xamlRoot;
        }

        using var registration = cancellationToken.Register(dialog.Hide);
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
