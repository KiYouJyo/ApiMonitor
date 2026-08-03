using ApiMonitor.Models;
using ApiMonitor.ViewModels;
using ApiMonitor.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Services;

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

    public async Task<bool> ConfirmDeleteAsync(
        string accountName,
        string providerDisplayName,
        CancellationToken cancellationToken)
    {
        var xamlRoot = _xamlRootProvider?.Invoke();
        if (xamlRoot is null)
        {
            _log?.Error("无法显示确认对话框：XamlRoot 为空。");
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = L10n.Get("Dialog.DeleteAccountTitle"),
            Content = L10n.Format("Dialog.DeleteAccountConfirm", accountName, providerDisplayName) +
                L10n.Get("Dialog.DeleteAccountScope") +
                L10n.Get("Dialog.DeleteAccountIrreversible"),
            PrimaryButtonText = L10n.Get("Dialog.DeleteAccountTitle"),
            CloseButtonText = L10n.Get("Common.Cancel"),
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

    public async Task<FirstCloseChoice> ShowFirstCloseExplanationAsync(CancellationToken cancellationToken)
    {
        var xamlRoot = _xamlRootProvider?.Invoke();
        if (xamlRoot is null)
        {
            _log?.Error("无法显示首次关闭说明对话框：XamlRoot 为空。");
            return FirstCloseChoice.Hide;
        }

        var dialog = new ContentDialog
        {
            Title = L10n.Get("Dialog.HideToTrayTitle"),
            Content = L10n.Get("Dialog.HideToTrayMessage"),
            PrimaryButtonText = L10n.Get("Dialog.HideAndRun"),
            SecondaryButtonText = L10n.Get("Dialog.DontAskAgain"),
            CloseButtonText = L10n.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        try
        {
            dialog.XamlRoot = xamlRoot;
            using var registration = cancellationToken.Register(dialog.Hide);
            var result = await dialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => FirstCloseChoice.Hide,
                ContentDialogResult.Secondary => FirstCloseChoice.HideAndDontAskAgain,
                _ => FirstCloseChoice.Cancel,
            };
        }
        catch (Exception ex)
        {
            _log?.Error($"显示首次关闭说明对话框失败: {ex.GetType().Name}");
            return FirstCloseChoice.Hide;
        }
    }

    /// <summary>v0.6.0：语言切换重启确认对话框（立即重启 / 稍后）。</summary>
    public async Task<bool> ConfirmRestartAsync(CancellationToken cancellationToken)
    {
        var xamlRoot = _xamlRootProvider?.Invoke();
        if (xamlRoot is null)
        {
            // XamlRoot 不可用时默认不重启（提示手动处理）。
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = L10n.Get("Dialog.RestartTitle"),
            Content = L10n.Get("Dialog.RestartMessage"),
            PrimaryButtonText = L10n.Get("Settings.RestartNow"),
            CloseButtonText = L10n.Get("Settings.Later"),
            DefaultButton = ContentDialogButton.Primary,
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
            _log?.Error($"显示重启确认对话框失败: {ex.GetType().Name}");
            return false;
        }
    }
}
