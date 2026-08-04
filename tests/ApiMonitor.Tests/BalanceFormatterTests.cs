using ApiMonitor.Helpers;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class BalanceFormatterTests
{
    [Theory]
    [InlineData(0d, "0.00")]
    [InlineData(3.15d, "3.15")]
    [InlineData(13.15d, "13.15")]
    [InlineData(123.45d, "123.45")]
    [InlineData(9999.99d, "9999.99")]
    [InlineData(-1.25d, "-1.25")]
    public void FormatsCommonFloatingWindowAmountsWithoutTruncation(double value, string expected)
    {
        Assert.Equal(expected, BalanceFormatter.Format((decimal)value));
    }
}
