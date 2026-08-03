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
    private readonly AppLog? _log;
    private Func<XamlRoot?>? _xamlRootProvider;

    public DialogService(IAccountManager accountManager, AppLog? log = null)
    {
        _accountManager = accountManager;
        _log = log;
    }

    public void Attach(Func<XamlRoot?> xamlRootProvider) => _xamlRootProvider = xamlRootProvider;

    public async Task<AccountEditorResult?> ShowAccountEditorAsync(
        AccountEditorContext context,
        CancellationToken cancellationToken)
    {
        var xamlRoot = _xamlRootProvider?.Invoke();
        if (xamlRoot is null)
        {
            _log?.Error("无法显示账户对话框：XamlRoot 为空。");
            return null;
        }

        try
        {
            var viewModel = new AccountEditorViewModel(_accountManager, context);
            var dialog = new AccountEditorDialog(viewModel) { XamlRoot = xamlRoot };

            using var registration = cancellationToken.Register(dialog.Hide);
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? dialog.Result : null;
        }
        catch (Exception ex)
        {
            _log?.Error($"显示账户对话框失败: {ex}");
            return null;
        }
    }

    public async Task<bool> ConfirmDeleteAsync(string accountName, CancellationToken cancellationToken)
    {
        var xamlRoot = _xamlRootProvider?.Invoke();
        if (xamlRoot is null)
        {
            _log?.Error("无法显示确认对话框：XamlRoot 为空。");
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = "删除账户",
            Content = $"确定要删除账户“{accountName}”吗？其保存的 API Key 与本地余额快照也会一并删除。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        try
        {
            dialog.XamlRoot = xamlRoot;

            using var registration = cancellationToken.Register(dialog.Hide);
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            _log?.Error($"显示确认对话框失败: {ex.GetType().Name}");
            return false;
        }
    }

    public async Task ShowHistoryAsync(string accountId, CancellationToken cancellationToken)
    {
        var xamlRoot = _xamlRootProvider?.Invoke();
        if (xamlRoot is null)
        {
            _log?.Error("无法显示历史对话框：XamlRoot 为空。");
            return;
        }

        try
        {
            var viewModel = new BalanceHistoryViewModel(_accountManager, accountId, _log);
            var dialog = new BalanceHistoryDialog(viewModel) { XamlRoot = xamlRoot };

            using var registration = cancellationToken.Register(dialog.Hide);
            _ = viewModel.LoadAsync();
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            _log?.Error($"显示历史对话框失败: {ex}");
        }
    }
}
