using ApiMonitor.Services;
using Xunit;

namespace ApiMonitor.Tests;

public sealed class DelayedClipboardGuardTests
{
    [Fact]
    public async Task UnchangedClipboard_IsCleared()
    {
        bool cleared = false;
        var guard = new DelayedClipboardGuard();

        await guard.RunAsync(
            "sk-test-only-not-real",
            TimeSpan.FromMilliseconds(20),
            () => Task.FromResult<string?>("sk-test-only-not-real"),
            () => cleared = true,
            CancellationToken.None);

        Assert.True(cleared);
    }

    [Fact]
    public async Task ReplacedClipboard_IsNotCleared()
    {
        bool cleared = false;
        var guard = new DelayedClipboardGuard();

        await guard.RunAsync(
            "sk-test-only-not-real",
            TimeSpan.FromMilliseconds(20),
            () => Task.FromResult<string?>("user-new-content"),
            () => cleared = true,
            CancellationToken.None);

        Assert.False(cleared);
    }

    [Fact]
    public async Task CancelledDelay_DoesNotClear()
    {
        bool cleared = false;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var guard = new DelayedClipboardGuard();

        await guard.RunAsync(
            "sk-test-only-not-real",
            TimeSpan.FromMinutes(1),
            () => Task.FromResult<string?>("sk-test-only-not-real"),
            () => cleared = true,
            cts.Token);

        Assert.False(cleared);
    }

    [Fact]
    public async Task ReadFailure_DoesNotClearOrThrow()
    {
        bool cleared = false;
        var guard = new DelayedClipboardGuard();

        await guard.RunAsync(
            "sk-test-only-not-real",
            TimeSpan.FromMilliseconds(20),
            () => Task.FromException<string?>(new InvalidOperationException("剪贴板被占用")),
            () => cleared = true,
            CancellationToken.None);

        Assert.False(cleared);
    }
}
