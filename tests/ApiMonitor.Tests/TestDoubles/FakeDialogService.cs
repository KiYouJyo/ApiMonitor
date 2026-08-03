using ApiMonitor.Models;
using ApiMonitor.Services;

namespace ApiMonitor.Tests.TestDoubles;

public sealed class FakeDialogService : IDialogService
{
    public AccountEditorResult? EditorResult { get; set; }

    public bool ConfirmDeleteResult { get; set; } = true;

    public Task<AccountEditorResult?> ShowAccountEditorAsync(
        AccountEditorContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(EditorResult);

    public Task<bool> ConfirmDeleteAsync(string accountName, CancellationToken cancellationToken) =>
        Task.FromResult(ConfirmDeleteResult);

    public Task ShowHistoryAsync(string accountId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
