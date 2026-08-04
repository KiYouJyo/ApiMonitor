using ApiMonitor.ViewModels;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class FloatingAmountDisplayTests
{
    [Theory]
    [InlineData("0.00", "CNY", 40)]
    [InlineData("3.15", "CNY", 40)]
    [InlineData("13.15", "CNY", 40)]
    [InlineData("123.45", "USD", 34)]
    [InlineData("999.99", "Credits", 28)]
    [InlineData("9999.99", "Credits", 24)]
    [InlineData("-1.25", "Credits", 28)]
    [InlineData("未知", "", 46)]
    public void SelectFontSizeUsesStableSafeBuckets(string amount, string unit, double expected)
    {
        Assert.Equal(expected, FloatingAmountDisplay.SelectFontSize(amount, unit));
    }
}
