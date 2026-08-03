namespace ApiBalanceMonitor.ViewModels;

/// <summary>账户卡片中单行币种余额的展示模型。</summary>
public sealed class BalanceLine
{
    public string Currency { get; }

    public decimal Total { get; }

    public string TotalText { get; }

    public string GrantedText { get; }

    public string ToppedUpText { get; }

    public string LineText =>
        $"{Currency} · 总额 {TotalText} · 赠送 {GrantedText} · 充值 {ToppedUpText}";

    public BalanceLine(
        string currency,
        decimal total,
        string totalText,
        string grantedText,
        string toppedUpText)
    {
        Currency = currency;
        Total = total;
        TotalText = totalText;
        GrantedText = grantedText;
        ToppedUpText = toppedUpText;
    }
}
