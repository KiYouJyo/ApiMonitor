using ApiMonitor.Models;
using ApiMonitor.Services;

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
/// 凭据输入字段类型（v0.9.0）：文本（文本框）或布尔（开关）。
/// 用于自托管配置中的“允许 HTTP”“启用管理状态探测”等显式确认项。
/// </summary>
public enum ProviderConfigFieldKind
{
    Text,
    Boolean,
}

/// <summary>
/// 非敏感 Provider 配置字段声明（v0.9.0 扩展 kind）。
/// 布尔字段用于需要用户显式确认的选项（如允许 HTTP）。
/// </summary>
public sealed record ProviderConfigField(
    string FieldId,
    string LabelKey,
    string HintKey,
    bool IsRequired,
    string? PlaceholderKey = null,
    ProviderConfigFieldKind Kind = ProviderConfigFieldKind.Text);

/// <summary>
/// 凭据槽位声明（v0.9.0）：多字段凭据（Key+SK、Basic 用户名+密码、
/// Bearer/QueryToken）的输入定义。所有槽位值独立存入 Credential Locker。
/// 可选条件显示：仅当指定配置字段等于指定值时显示该槽位
/// （如 OGC 的 username/password 仅当 authMode=basic 时显示）。
/// </summary>
public sealed record ProviderCredentialSlot(
    string SlotId,
    string LabelKey,
    string HintKey,
    bool IsRequired,
    bool IsSecret = true,
    string? PlaceholderKey = null,
    string? ConditionalOnConfigFieldId = null,
    string? ConditionalOnConfigValue = null);

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
    bool AllowCustomEndpoint = false,
    ProviderCategory Category = ProviderCategory.ArtificialIntelligence,
    IReadOnlyList<ProviderCapability>? Capabilities = null,
    IReadOnlyList<ProviderCredentialSlot>? CredentialSlots = null,
    IReadOnlyList<MetricKind>? DetailedMetricKinds = null,
    bool ProbeConsumesQuota = false,
    string ProbeDescriptionKey = "")
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

    /// <summary>Provider 分类（v0.9.0；默认 AI，保持旧 Provider 行为）。</summary>
    public ProviderCategory EffectiveCategory => Category;

    /// <summary>Provider 能力列表（v0.9.0）。</summary>
    public IReadOnlyList<ProviderCapability> EffectiveCapabilities =>
        Capabilities ?? Array.Empty<ProviderCapability>();

    /// <summary>
    /// 凭据槽位（v0.9.0）。未声明时回退为单个 primary 槽位，
    /// 保证旧 Provider（仅一个 API Key）的编辑体验不变。
    /// </summary>
    public IReadOnlyList<ProviderCredentialSlot> EffectiveCredentialSlots =>
        CredentialSlots is { Count: > 0 }
            ? CredentialSlots
            : new[]
            {
                new ProviderCredentialSlot(
                    ApiMonitor.Models.CredentialSlots.Primary,
                    "Dialog.ApiKey.Header",
                    ApiKeyInputHint,
                    IsRequired: true),
            };

    /// <summary>地理/GIS 指标详细类型（v0.9.0）。</summary>
    public IReadOnlyList<MetricKind> EffectiveDetailedMetricKinds =>
        DetailedMetricKinds ?? Array.Empty<MetricKind>();

    /// <summary>一次主动探测是否消耗一次 API 调用额度（v0.9.0）。</summary>
    public bool EffectiveProbeConsumesQuota => ProbeConsumesQuota;

    /// <summary>探测服务说明（v0.9.0；账户卡片“探测服务”展示）。</summary>
    public string ProbeDescription =>
        string.IsNullOrWhiteSpace(ProbeDescriptionKey)
            ? string.Empty
            : L10n.Get(ProbeDescriptionKey);
}
