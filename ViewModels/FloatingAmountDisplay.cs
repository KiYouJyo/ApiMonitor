namespace ApiMonitor.ViewModels;

/// <summary>
/// 悬浮窗额度显示的集中布局模型。只提供有限字号档位，避免运行时测量和窗口尺寸变化。
/// </summary>
public sealed record FloatingAmountDisplay(
    string AmountText,
    string UnitText,
    double AmountFontSize,
    string AccountText,
    string ProviderText,
    string StatusText)
{
    public static double SelectFontSize(string amountText, string unitText)
    {
        if (string.IsNullOrWhiteSpace(unitText))
        {
            return 46;
        }

        int length = (amountText ?? string.Empty).Length + (unitText ?? string.Empty).Length;
        return length switch
        {
            <= 6 => 40,
            <= 8 => 36,
            <= 11 => 32,
            <= 13 => 28,
            _ => 24,
        };
    }
}
