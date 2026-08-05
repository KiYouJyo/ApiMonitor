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

    /// <summary>Provider 能力元数据（注册表与“添加账户”页面动态读取）。</summary>
    ProviderInfo Info { get; }

    Task<BalanceQueryResult> QueryBalanceAsync(
        ApiAccount account,
        string apiKey,
        CancellationToken cancellationToken) =>
        QueryBalanceAsync(
            account,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CredentialSlots.Primary] = apiKey ?? string.Empty,
            },
            cancellationToken);

    /// <summary>
    /// v0.9.0：多槽位凭据查询入口。默认实现把 primary 槽位传给旧签名，
    /// 保证五个既有 AI Provider 与它们的测试完全不变；
    /// 新地理/GIS Provider 重写此方法以读取 Key+SK、用户名+密码等槽位。
    /// </summary>
    Task<BalanceQueryResult> QueryBalanceAsync(
        ApiAccount account,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken)
    {
        credentials.TryGetValue(CredentialSlots.Primary, out var primary);
        return QueryBalanceAsync(account, primary ?? string.Empty, cancellationToken);
    }
}
