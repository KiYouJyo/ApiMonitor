using ApiMonitor.Models;

namespace ApiMonitor.Services;

/// <summary>
/// 地理/GIS 服务指标构造器（v0.9.0）。
/// 统一生成五件套主指标（service.availability / service.latency.ms /
/// credential.status / permission.status / quota.state）与计数/布尔/类型指标。
/// 未提供精确数值的 quota.limit/used/remaining/reset_at 一律不生成，
/// 保证 UI 永不显示伪造的余量或 0。
/// </summary>
public static class GeospatialMetricFactory
{
    public static BalanceMetric ServiceAvailability(string providerId, GeospatialStatus status) =>
        StatusMetric(providerId, "service.availability", MetricKind.ServiceAvailability, status);

    public static BalanceMetric Latency(string providerId, long milliseconds) =>
        new()
        {
            MetricId = $"{providerId}:service.latency.ms",
            DisplayName = L10n.Get("Metric.LatencyName"),
            Unit = "ms",
            Kind = BalanceMetricKind.Other,
            DetailedKind = MetricKind.LatencyMilliseconds,
            ValueKind = MetricValueKind.Integer,
            IntegerValue = milliseconds,
        };

    public static BalanceMetric CredentialStatus(string providerId, GeospatialStatus status) =>
        StatusMetric(providerId, "credential.status", MetricKind.CredentialStatus, status);

    public static BalanceMetric PermissionStatus(string providerId, GeospatialStatus status) =>
        StatusMetric(providerId, "permission.status", MetricKind.PermissionStatus, status);

    public static BalanceMetric QuotaState(string providerId, GeospatialStatus status) =>
        StatusMetric(providerId, "quota.state", MetricKind.QuotaState, status);

    public static BalanceMetric StatusMetric(
        string providerId,
        string suffix,
        MetricKind kind,
        GeospatialStatus status) =>
        new()
        {
            MetricId = $"{providerId}:{suffix}",
            DisplayName = MetricDisplayName(kind),
            Unit = string.Empty,
            Kind = BalanceMetricKind.Other,
            DetailedKind = kind,
            ValueKind = MetricValueKind.Status,
            StatusValue = status.ToString(),
        };

    /// <summary>
    /// 四个公共地图 Provider 的统一五件套指标。
    /// quota.limit/used/remaining/reset_at 一律不生成（官方无精确查询接口）。
    /// </summary>
    public static IReadOnlyList<BalanceMetric> BuildMapMetricSet(
        string providerId,
        GeospatialStatus status,
        long latencyMilliseconds) =>
        new[]
        {
            ServiceAvailability(providerId, status),
            Latency(providerId, latencyMilliseconds),
            CredentialStatus(providerId, CredentialStatusFor(status)),
            PermissionStatus(providerId, PermissionStatusFor(status)),
            QuotaState(providerId, QuotaStateFor(status)),
        };

    private static GeospatialStatus CredentialStatusFor(GeospatialStatus status) =>
        status is GeospatialStatus.CredentialInvalid
            or GeospatialStatus.KeyTypeMismatch
            or GeospatialStatus.SignatureInvalid
            or GeospatialStatus.ConfigurationMissing
            ? status
            : GeospatialStatus.Healthy;

    private static GeospatialStatus PermissionStatusFor(GeospatialStatus status) =>
        status is GeospatialStatus.PermissionDenied
            or GeospatialStatus.IpWhitelistDenied
            or GeospatialStatus.RefererDomainDenied
            or GeospatialStatus.ServiceNotEnabled
            ? status
            : GeospatialStatus.Healthy;

    private static GeospatialStatus QuotaStateFor(GeospatialStatus status) =>
        status is GeospatialStatus.QuotaExceeded or GeospatialStatus.RateLimited
            ? status
            : status == GeospatialStatus.Healthy
                ? GeospatialStatus.Healthy
                : GeospatialStatus.Unknown;

    public static BalanceMetric TypeMetric(string providerId, string suffix, string value) =>
        new()
        {
            MetricId = $"{providerId}:{suffix}",
            DisplayName = L10n.Get("Metric.ServiceTypeName"),
            Unit = string.Empty,
            Kind = BalanceMetricKind.Other,
            ValueKind = MetricValueKind.Status,
            StatusValue = value,
        };

    public static BalanceMetric VersionMetric(string providerId, string suffix, string value) =>
        new()
        {
            MetricId = $"{providerId}:{suffix}",
            DisplayName = L10n.Get("Metric.ServiceVersionName"),
            Unit = string.Empty,
            Kind = BalanceMetricKind.Other,
            ValueKind = MetricValueKind.Status,
            StatusValue = value,
        };

    public static BalanceMetric CountMetric(
        string providerId,
        string suffix,
        MetricKind kind,
        long count) =>
        new()
        {
            MetricId = $"{providerId}:{suffix}",
            DisplayName = MetricDisplayName(kind),
            Unit = string.Empty,
            Kind = BalanceMetricKind.Other,
            DetailedKind = kind,
            ValueKind = MetricValueKind.Integer,
            IntegerValue = count,
        };

    public static BalanceMetric BooleanMetric(
        string providerId,
        string suffix,
        string displayNameKey,
        bool value) =>
        new()
        {
            MetricId = $"{providerId}:{suffix}",
            DisplayName = L10n.Get(displayNameKey),
            Unit = string.Empty,
            Kind = BalanceMetricKind.Other,
            ValueKind = MetricValueKind.Boolean,
            BooleanValue = value,
        };

    public static BalanceMetric TimestampMetric(
        string providerId,
        string suffix,
        string displayNameKey,
        DateTimeOffset value) =>
        new()
        {
            MetricId = $"{providerId}:{suffix}",
            DisplayName = L10n.Get(displayNameKey),
            Unit = string.Empty,
            Kind = BalanceMetricKind.Other,
            ValueKind = MetricValueKind.Timestamp,
            TimestampValue = value,
        };

    /// <summary>状态展示文本（地理/GIS 卡片与悬浮窗共用）。</summary>
    public static string StatusText(GeospatialStatus status) =>
        status switch
        {
            GeospatialStatus.Healthy => L10n.Get("Geo.StatusHealthy"),
            GeospatialStatus.Unknown => L10n.Get("Geo.StatusUnknown"),
            GeospatialStatus.CredentialInvalid => L10n.Get("Geo.StatusCredentialInvalid"),
            GeospatialStatus.KeyTypeMismatch => L10n.Get("Geo.StatusKeyTypeMismatch"),
            GeospatialStatus.IpWhitelistDenied => L10n.Get("Geo.StatusIpWhitelistDenied"),
            GeospatialStatus.RefererDomainDenied => L10n.Get("Geo.StatusRefererDomainDenied"),
            GeospatialStatus.SignatureInvalid => L10n.Get("Geo.StatusSignatureInvalid"),
            GeospatialStatus.PermissionDenied => L10n.Get("Geo.StatusPermissionDenied"),
            GeospatialStatus.ServiceNotEnabled => L10n.Get("Geo.StatusServiceNotEnabled"),
            GeospatialStatus.QuotaExceeded => L10n.Get("Geo.StatusQuotaExceeded"),
            GeospatialStatus.RateLimited => L10n.Get("Geo.StatusRateLimited"),
            GeospatialStatus.NetworkUnavailable => L10n.Get("Geo.StatusNetworkUnavailable"),
            GeospatialStatus.Timeout => L10n.Get("Geo.StatusTimeout"),
            GeospatialStatus.TlsFailure => L10n.Get("Geo.StatusTlsFailure"),
            GeospatialStatus.ProviderError => L10n.Get("Geo.StatusProviderError"),
            GeospatialStatus.InvalidResponse => L10n.Get("Geo.StatusInvalidResponse"),
            GeospatialStatus.ConfigurationMissing => L10n.Get("Geo.StatusConfigurationMissing"),
            _ => status.ToString(),
        };

    /// <summary>从持久化/解析值解析状态；无法识别返回 Unknown。</summary>
    public static GeospatialStatus Parse(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Enum.TryParse<GeospatialStatus>(value, ignoreCase: true, out var status)
            ? status
            : GeospatialStatus.Unknown;

    private static string MetricDisplayName(MetricKind kind) =>
        kind switch
        {
            MetricKind.ServiceAvailability => L10n.Get("Metric.ServiceAvailabilityName"),
            MetricKind.CredentialStatus => L10n.Get("Metric.CredentialStatusName"),
            MetricKind.PermissionStatus => L10n.Get("Metric.PermissionStatusName"),
            MetricKind.QuotaState => L10n.Get("Metric.QuotaStateName"),
            MetricKind.ServiceCount => L10n.Get("Metric.ServiceCountName"),
            MetricKind.LayerCount => L10n.Get("Metric.LayerCountName"),
            MetricKind.QuotaRemaining => L10n.Get("Metric.QuotaRemainingName"),
            MetricKind.QuotaUsed => L10n.Get("Metric.QuotaUsedName"),
            MetricKind.QuotaLimit => L10n.Get("Metric.QuotaLimitName"),
            _ => kind.ToString(),
        };
}
