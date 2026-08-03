namespace ApiBalanceMonitor.Services;

/// <summary>
/// 剪贴板清理判定：仅当当前内容仍等于刚复制的文本时才允许清除，
/// 避免覆盖用户后续复制的新内容。纯逻辑，便于单元测试。
/// </summary>
public static class ClipboardPolicy
{
    public static bool ShouldClear(string copiedText, string? currentText) =>
        string.Equals(copiedText, currentText, StringComparison.Ordinal);
}
