using ApiMonitor.Models;

namespace ApiMonitor.Providers;

/// <summary>
/// Provider 支持的凭据类型（如 DeepSeek 普通 API Key、OpenRouter 的
/// 普通 API Key 与 Management Key）。凭据模式属于非敏感账户选项，
/// 保存在账户模型中；密钥本身只进 ISecretStore。
/// </summary>
public sealed record ProviderCredentialOption(
    string CredentialTypeId,
    string DisplayName,
    string Description,
    bool IsDefault);

/// <summary>
/// Provider 的稳定能力元数据，由各 Provider 实现自声明，
/// 用于从注册表动态生成“添加账户”页面（不把 Provider 选择写死在 XAML）。
/// </summary>
public sealed record ProviderInfo(
    string ProviderId,
    string DisplayName,
    string Description,
    bool SupportsAccountBalance,
    bool SupportsKeyQuota,
    IReadOnlyList<BalanceMetricKind> SupportedMetricKinds,
    IReadOnlyList<ProviderCredentialOption> CredentialOptions,
    string ApiKeyInputHint,
    string HelpUrl,
    bool SupportsTestConnection)
{
    public ProviderCredentialOption DefaultCredentialOption =>
        CredentialOptions.FirstOrDefault(o => o.IsDefault)
        ?? CredentialOptions.FirstOrDefault()
        ?? new ProviderCredentialOption("api-key", "API Key", string.Empty, true);
}
