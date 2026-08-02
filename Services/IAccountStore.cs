using ApiBalanceMonitor.Models;

namespace ApiBalanceMonitor.Services;

public interface IAccountStore
{
    Task<AccountsFileLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyList<ApiAccount> accounts, CancellationToken cancellationToken);
}

public sealed class AccountsFileLoadResult
{
    public required IReadOnlyList<ApiAccount> Accounts { get; init; }

    public string? RecoveryMessage { get; init; }
}
