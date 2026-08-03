using System.Text.Json.Serialization;

namespace ApiMonitor.Providers.Dto;

/// <summary>
/// DeepSeek 官方余额接口的独立 DTO，只负责接收 JSON，
/// 通过 Provider 映射到通用领域模型，绝不直接绑定到页面。
/// 未知新增字段会被 System.Text.Json 默认忽略，不影响解析。
/// </summary>
public sealed class DeepSeekBalanceResponse
{
    [JsonPropertyName("is_available")]
    public bool IsAvailable { get; set; }

    [JsonPropertyName("balance_infos")]
    public List<DeepSeekBalanceInfo>? BalanceInfos { get; set; }
}

public sealed class DeepSeekBalanceInfo
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("total_balance")]
    public string? TotalBalance { get; set; }

    [JsonPropertyName("granted_balance")]
    public string? GrantedBalance { get; set; }

    [JsonPropertyName("topped_up_balance")]
    public string? ToppedUpBalance { get; set; }
}
