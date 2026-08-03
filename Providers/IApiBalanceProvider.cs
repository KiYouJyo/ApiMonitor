using ApiMonitor.Models;

namespace ApiMonitor.Providers;

/// <summary>
/// 通用 API 余额 Provider 抽象。实现负责把各自接口映射到通用领域模型，
/// 避免把 Provider 特有字段扩散到 UI。
/// </summary>
public interface IApiBalanceProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    Task<BalanceQueryResult> QueryBalanceAsync(
        ApiAccount account,
        string apiKey,
        CancellationToken cancellationToken);
}
