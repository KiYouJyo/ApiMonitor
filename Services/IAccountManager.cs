using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Providers;

namespace ApiBalanceMonitor.Services;

/// <summary>
/// 账户与余额查询的门面服务：ViewModel 只依赖此接口，
/// 不直接接触 HttpClient、Credential Locker 或文件系统。
/// </summary>
public interface IAccountManager
{
    IReadOnlyList<ProviderInfo> Providers { get; }

    IReadOnlyList<string> RecoveryMessages { get; }

    Task<IReadOnlyList<ApiAccount>> LoadAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiAccount>> GetAllAccountsAsync(CancellationToken cancellationToken);

    Task<AccountBalanceRecord?> GetRecordAsync(string accountId, CancellationToken cancellationToken);

    Task<BalanceQueryResult> TestConnectionAsync(
        string providerId,
        string? apiKey,
        string? accountId,
        CancellationToken cancellationToken);

    Task<ApiAccount> SaveAccountAsync(
        string? accountId,
        string providerId,
        string displayName,
        string? newApiKey,
        CancellationToken cancellationToken);

    Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken);

    Task<BalanceQueryResult> RefreshAccountAsync(string accountId, CancellationToken cancellationToken);
}
