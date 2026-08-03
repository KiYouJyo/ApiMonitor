using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class ClipboardPolicyTests
{
    [Theory]
    [InlineData("sk-test-only-not-real", "sk-test-only-not-real", true)]
    [InlineData("sk-test-only-not-real", "user-new-content", false)]
    [InlineData("sk-test-only-not-real", null, false)]
    [InlineData("sk-test-only-not-real", "", false)]
    public void ShouldClear_ComparesCurrentContentOrdinal(
        string copied,
        string? current,
        bool expected)
    {
        Assert.Equal(expected, ClipboardPolicy.ShouldClear(copied, current));
    }
}
