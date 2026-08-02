using System.Globalization;

namespace ApiBalanceMonitor.Helpers;

/// <summary>余额显示格式化，金额始终使用 decimal。</summary>
public static class BalanceFormatter
{
    public static string Format(decimal value) =>
        value.ToString("0.00", CultureInfo.CurrentCulture);
}
