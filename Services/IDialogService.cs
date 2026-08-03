using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>首次隐藏到托盘前说明对话框的用户选择。</summary>
public enum FirstCloseChoice
{
    /// <summary>隐藏到通知区域，本次会话不再提示。</summary>
    Hide,

    /// <summary>隐藏并持久化“不再提示”。</summary>
    HideAndDontAskAgain,

    /// <summary>取消关闭，保持窗口显示。</summary>
    Cancel,
}

/// <summary>对话框抽象，让 MainViewModel 保持可测试。</summary>
public interface IDialogService
{
    Task<AccountEditorResult?> ShowAccountEditorAsync(
        AccountEditorContext context,
        CancellationToken cancellationToken);

    Task<bool> ConfirmDeleteAsync(
        string accountName,
        string providerDisplayName,
        CancellationToken cancellationToken);

    /// <summary>显示指定账户的余额历史对话框。</summary>
    Task ShowHistoryAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>首次隐藏到托盘前的说明对话框（窗口内显示，不使用系统通知/托盘气泡）。</summary>
    Task<FirstCloseChoice> ShowFirstCloseExplanationAsync(CancellationToken cancellationToken);
}
