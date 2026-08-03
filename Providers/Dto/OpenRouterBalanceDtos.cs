using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiMonitor.Providers.Dto;

/// <summary>
/// OpenRouter 官方 DTO（GET /api/v1/key）。独立 DTO 绝不泄漏到 UI，
/// 只负责解析官方响应并映射到通用领域模型。
/// 未知新增字段由 System.Text.Json 默认忽略，保持向前兼容。
/// </summary>
public sealed class OpenRouterKeyResponse
{
    [JsonPropertyName("data")]
    public OpenRouterKeyData? Data { get; set; }
}

public sealed class OpenRouterKeyData
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("limit")]
    public JsonElement? Limit { get; set; }

    [JsonPropertyName("limit_reset")]
    public string? LimitReset { get; set; }

    [JsonPropertyName("limit_remaining")]
    public JsonElement? LimitRemaining { get; set; }

    [JsonPropertyName("usage")]
    public JsonElement? Usage { get; set; }

    [JsonPropertyName("usage_daily")]
    public JsonElement? UsageDaily { get; set; }

    [JsonPropertyName("usage_weekly")]
    public JsonElement? UsageWeekly { get; set; }

    [JsonPropertyName("usage_monthly")]
    public JsonElement? UsageMonthly { get; set; }

    [JsonPropertyName("byok_usage")]
    public JsonElement? ByokUsage { get; set; }

    [JsonPropertyName("is_free_tier")]
    public bool? IsFreeTier { get; set; }
}

/// <summary>
/// OpenRouter 官方 DTO（GET /api/v1/credits）。Management Key 专用。
/// </summary>
public sealed class OpenRouterCreditsResponse
{
    [JsonPropertyName("total_credits")]
    public JsonElement? TotalCredits { get; set; }

    [JsonPropertyName("total_usage")]
    public JsonElement? TotalUsage { get; set; }
}
