namespace ApiBalanceMonitor.Providers;

/// <summary>Provider 的稳定元数据，用于 UI 展示与注册表查询。</summary>
public sealed record ProviderInfo(string ProviderId, string DisplayName);
