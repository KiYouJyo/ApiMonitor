using ApiMonitor.Models;

namespace ApiMonitor.Providers;

/// <summary>
/// Provider 需要的非敏感账户配置字段（如 xAI Team ID）。
/// 字段值保存在账户元数据（accounts.json / 备份）中，绝不保存密钥。
/// 界面根据 ProviderInfo.ConfigFields 动态生成输入框。
/// </summary>
public sealed record ProviderConfigField(
    string FieldId,
    string LabelKey,
    string HintKey,
    bool IsRequired,
    string? PlaceholderKey = null);

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
    bool SupportsTestConnection,
    string DefaultBaseUrl = "",
    IReadOnlyList<ProviderConfigField>? ConfigFields = null,
    string PrimaryMetricId = "",
    string? Currency = null,
    bool SupportsMultiCurrency = false,
    bool SupportsBreakdown = false,
    bool SupportsCredentialValidation = false,
    bool AllowCustomEndpoint = false)
{
    public ProviderCredentialOption DefaultCredentialOption =>
        CredentialOptions.FirstOrDefault(o => o.IsDefault)
        ?? CredentialOptions.FirstOrDefault()
        ?? new ProviderCredentialOption("api-key", "API Key", string.Empty, true);

    /// <summary>非敏感配置字段（可能为空）。</summary>
    public IReadOnlyList<ProviderConfigField> EffectiveConfigFields =>
        ConfigFields ?? Array.Empty<ProviderConfigField>();

    /// <summary>必填的非敏感配置字段（如 xAI Team ID）。</summary>
    public IReadOnlyList<ProviderConfigField> RequiredConfigFields =>
        EffectiveConfigFields.Where(f => f.IsRequired).ToList();
}
