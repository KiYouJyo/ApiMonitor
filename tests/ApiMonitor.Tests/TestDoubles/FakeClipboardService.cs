using ApiMonitor.Services;

namespace ApiMonitor.Tests.TestDoubles;

public sealed class FakeClipboardService : IClipboardService
{
    public List<string> SetCalls { get; } = new();

    public TimeSpan? LastClearAfter { get; private set; }

    public Task SetSensitiveTextAsync(
        string text,
        TimeSpan clearAfter,
        CancellationToken cancellationToken)
    {
        SetCalls.Add(text);
        LastClearAfter = clearAfter;
        return Task.CompletedTask;
    }
}
