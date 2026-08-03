using ApiBalanceMonitor.Models;

namespace ApiBalanceMonitor.Services;

/// <summary>对话框抽象，让 MainViewModel 保持可测试。</summary>
public interface IDialogService
{
    Task<AccountEditorResult?> ShowAccountEditorAsync(
        AccountEditorContext context,
        CancellationToken cancellationToken);

    Task<bool> ConfirmDeleteAsync(string accountName, CancellationToken cancellationToken);

    /// <summary>显示指定账户的余额历史对话框。</summary>
    Task ShowHistoryAsync(string accountId, CancellationToken cancellationToken);
}
