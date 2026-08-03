using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Tests.TestDoubles;

public sealed class FakeDialogService : IDialogService
{
    public AccountEditorResult? EditorResult { get; set; }

    public bool ConfirmDeleteResult { get; set; } = true;

    public FirstCloseChoice FirstCloseResult { get; set; } = FirstCloseChoice.Hide;

    public int FirstCloseCalls { get; private set; }

    public Task<AccountEditorResult?> ShowAccountEditorAsync(
        AccountEditorContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(EditorResult);

    public Task<bool> ConfirmDeleteAsync(
        string accountName,
        string providerDisplayName,
        CancellationToken cancellationToken) =>
        Task.FromResult(ConfirmDeleteResult);

    public Task ShowHistoryAsync(string accountId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<FirstCloseChoice> ShowFirstCloseExplanationAsync(CancellationToken cancellationToken)
    {
        FirstCloseCalls++;
        return Task.FromResult(FirstCloseResult);
    }
}
