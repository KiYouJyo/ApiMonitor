using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Services;

namespace ApiBalanceMonitor.Tests.TestDoubles;

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
}
