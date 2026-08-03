using ApiMonitor.Models;

namespace ApiMonitor.Services;

public interface IBalanceSnapshotStore : IBalanceHistoryStore
{
    Task<BalanceRecordsLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyList<AccountBalanceRecord> records, CancellationToken cancellationToken);
}

public sealed class BalanceRecordsLoadResult
{
    public required IReadOnlyList<AccountBalanceRecord> Records { get; init; }

    public string? RecoveryMessage { get; init; }
}
