namespace ApiMonitor.Models;

/// <summary>
/// 监测对象的通用分类（v0.9.0）：AI 平台、国内地图开放平台、自托管 GIS 服务。
/// 地理 Provider 绝不进入资金余额汇总。
/// </summary>
public enum ProviderCategory
{
    ArtificialIntelligence,
    Geospatial,
    GisServer,
}

/// <summary>
/// Provider 能力位（v0.9.0）。用于注册表、添加账户页面与刷新/通知策略。
/// </summary>
public enum ProviderCapability
{
    MonetaryBalance,
    CreditBalance,
    HealthProbe,
    CredentialValidation,
    PermissionValidation,
    QuotaStateDetection,
    ServiceCatalog,
    LatencyMeasurement,
}

/// <summary>
/// 通用指标的详细类型（v0.9.0）。与旧 <see cref="BalanceMetricKind"/> 并存：
/// AI Provider 沿用旧枚举（行为不变），地理/GIS 指标使用详细类型。
/// </summary>
public enum MetricKind
{
    MonetaryBalance,
    CreditBalance,
    QuotaRemaining,
    QuotaUsed,
    QuotaLimit,
    QuotaState,
    CredentialStatus,
    PermissionStatus,
    ServiceAvailability,
    LatencyMilliseconds,
    ServiceCount,
    LayerCount,
}

/// <summary>
/// 指标值的通用类型（v0.9.0）。旧 JSON 只有十进制金额；
/// 新字段全部可空，旧文件反序列化时自然得到 Decimal 默认值。
/// </summary>
public enum MetricValueKind
{
    Decimal,
    Integer,
    Boolean,
    Status,
    Timestamp,
}

/// <summary>
/// 地理/GIS 服务健康状态（v0.9.0）。状态值以字符串持久化，
/// 使用稳定英文标识，避免三语显示名进入存储。
/// </summary>
public enum GeospatialStatus
{
    Healthy,
    Unknown,
    CredentialInvalid,
    KeyTypeMismatch,
    IpWhitelistDenied,
    RefererDomainDenied,
    SignatureInvalid,
    PermissionDenied,
    ServiceNotEnabled,
    QuotaExceeded,
    RateLimited,
    NetworkUnavailable,
    Timeout,
    TlsFailure,
    ProviderError,
    InvalidResponse,
    ConfigurationMissing,
}

/// <summary>
/// 余额指标的稳定类型分类。阈值规则与历史记录引用稳定的
/// <see cref="BalanceMetric.MetricId"/>，而不是只以显示名称关联。
/// </summary>
public enum BalanceMetricKind
{
    /// <summary>货币余额（如 DeepSeek 的 CNY/USD 总余额）。</summary>
    MonetaryBalance,

    /// <summary>平台 Credits（如 OpenRouter 账户 Credits）。</summary>
    PlatformCredits,

    /// <summary>密钥额度（剩余额度 / 上限）。</summary>
    KeyQuota,

    /// <summary>累计或周期使用量。</summary>
    Usage,

    /// <summary>其他未分类指标。</summary>
    Other,
}

/// <summary>指标的附加展示值（不参与阈值判断，如 BYOK 使用量）。</summary>
public sealed class BalanceMetricAdditionalValue
{
    public required string Name { get; init; }

    public decimal Value { get; init; }

    public string? Unit { get; init; }
}

/// <summary>
/// 通用余额指标模型，替代 v0.4.0 面向 DeepSeek 的
/// <c>BalanceAmount</c>（Currency/TotalBalance/GrantedBalance/ToppedUpBalance）。
/// 所有金额与额度一律使用 <see cref="decimal"/>；数值未知时必须为 null，
/// 不得用 0 表示未知，也不得用极大数字表示无限额度。
/// </summary>
public sealed class BalanceMetric
{
    /// <summary>稳定指标 ID（如 deepseek:CNY:total、openrouter:credits:remaining）。</summary>
    public required string MetricId { get; init; }

    /// <summary>展示名称（如“CNY 总余额”“剩余 Credits”“密钥剩余额度”）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>单位（如 CNY、USD、credits、requests）。</summary>
    public required string Unit { get; init; }

    public BalanceMetricKind Kind { get; init; }

    /// <summary>可用余额/额度；null 表示 Provider 未提供该数值（未知）。</summary>
    public decimal? AvailableAmount { get; init; }

    /// <summary>总额/上限；null 表示未知。</summary>
    public decimal? TotalAmount { get; init; }

    /// <summary>累计使用量；null 表示未知。</summary>
    public decimal? UsedAmount { get; init; }

    /// <summary>赠送余额；null 表示该 Provider 不提供此语义（不得强制映射为 0）。</summary>
    public decimal? GrantedAmount { get; init; }

    /// <summary>充值余额；null 表示该 Provider 不提供此语义。</summary>
    public decimal? ToppedUpAmount { get; init; }

    /// <summary>是否允许为该指标设置低余额阈值。</summary>
    public bool IsThresholdSupported { get; init; }

    /// <summary>是否为无限额度（如密钥未设置额度上限）；不得用极大数值表示。</summary>
    public bool IsUnlimited { get; init; }

    /// <summary>附加展示值（如周期使用量、BYOK 使用量），不参与阈值判断。</summary>
    public IReadOnlyList<BalanceMetricAdditionalValue> AdditionalDisplayValues { get; init; } =
        Array.Empty<BalanceMetricAdditionalValue>();

    // ------------------------------------------------------------------
    // v0.9.0：通用值结构。旧 AI 指标只使用 AvailableAmount 等十进制字段，
    // ValueKind 保持 Decimal，以下新字段为 null；地理/GIS 指标使用新字段。
    // 旧 JSON 无这些字段时按默认值反序列化，无需提升 schemaVersion。
    // ------------------------------------------------------------------

    /// <summary>指标值的类型（v0.9.0；旧指标为 Decimal）。</summary>
    public MetricValueKind ValueKind { get; init; } = MetricValueKind.Decimal;

    /// <summary>指标详细类型（v0.9.0；旧 AI 指标为 null）。</summary>
    public MetricKind? DetailedKind { get; init; }

    /// <summary>状态值（ValueKind=Status，如 Healthy/CredentialInvalid）。</summary>
    public string? StatusValue { get; init; }

    /// <summary>布尔值（ValueKind=Boolean，如 expected-service.present）。</summary>
    public bool? BooleanValue { get; init; }

    /// <summary>整数/计数/延迟值（ValueKind=Integer，如 ms、服务数、图层数）。</summary>
    public long? IntegerValue { get; init; }

    /// <summary>时间戳值（ValueKind=Timestamp，如配额重置时间；未知为 null）。</summary>
    public DateTimeOffset? TimestampValue { get; init; }
}
