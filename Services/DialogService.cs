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
            Title = "删除账户",
            Content = $"确定要删除账户“{accountName}”（{providerDisplayName}）吗？\n\n" +
                "删除后，该账户的凭据、余额历史、阈值、通知设置与活动通知将一并删除。\n" +
                "此操作不可撤销。",
            PrimaryButtonText = "删除账户",
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
            Title = "隐藏到通知区域",
            Content = "ApiMonitor 将继续在通知区域运行。你可以单击右下角图标重新打开，或从托盘菜单退出。",
            PrimaryButtonText = "隐藏并继续运行",
            SecondaryButtonText = "不再提示",
            CloseButtonText = "取消",
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
            Title = "重启应用",
            Content = "语言更改将在重启应用后生效。是否立即重启？",
            PrimaryButtonText = "立即重启",
            CloseButtonText = "稍后",
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
