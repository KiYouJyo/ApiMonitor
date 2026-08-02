using ApiBalanceMonitor.Providers;

namespace ApiBalanceMonitor.Models;

/// <summary>
/// 打开账户编辑对话框所需的上下文。Provider 列表来自注册表，
/// UI 与模型不写死为只有 DeepSeek。
/// </summary>
public sealed class AccountEditorContext
{
    public string? AccountId { get; init; }

    public required IReadOnlyList<ProviderInfo> Providers { get; init; }

    public required string InitialProviderId { get; init; }

    public string? InitialDisplayName { get; init; }

    public bool HasStoredCredential { get; init; }
}

/// <summary>
/// 用户点击“保存”后由对话框返回的结果。ApiKey 为 null 表示沿用已有凭据。
/// </summary>
public sealed class AccountEditorResult
{
    public bool SaveRequested { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? ApiKey { get; set; }
}
