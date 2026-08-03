namespace ApiMonitor.Models;

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
}
