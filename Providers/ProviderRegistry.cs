namespace ApiMonitor.Providers;

/// <summary>
/// 代码内注册的 Provider 注册表（不采用反射加载不受信任 DLL 的插件机制）。
/// </summary>
public sealed class ProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IApiBalanceProvider> _providers;

    public ProviderRegistry(IEnumerable<IApiBalanceProvider> providers)
    {
        _providers = providers.ToDictionary(
            p => p.ProviderId,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IApiBalanceProvider> All => _providers.Values.ToList();

    public IApiBalanceProvider? GetById(string providerId) =>
        !string.IsNullOrWhiteSpace(providerId) && _providers.TryGetValue(providerId, out var provider)
            ? provider
            : null;
}
