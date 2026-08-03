using ApiBalanceMonitor.Models;
using ApiBalanceMonitor.Services;
using Xunit;

namespace ApiBalanceMonitor.Tests;

public sealed class HistoryRetentionTests
{
    private static BalanceHistoryEntry Entry(int index, DateTimeOffset at) =>
        new()
        {
            Id = $"id-{index:D5}",
            AccountId = "acct-1",
            ProviderId = "deepseek",
            SucceededAtUtc = at,
            Source = BalanceQuerySource.Manual,
            IsAvailable = true,
            Balances = Array.Empty<BalanceAmount>(),
        };

    [Fact]
    public void OlderThan90Days_IsRemoved()
    {
        var now = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var history = new[]
        {
            Entry(1, now.AddDays(-91)),
            Entry(2, now.AddDays(-89)),
        };

        var result = HistoryRetention.Apply(history, now);

        var single = Assert.Single(result);
        Assert.Equal("id-00002", single.Id);
    }

    [Fact]
    public void Over10000Entries_KeepsNewestOnly()
    {
        var now = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var history = Enumerable.Range(0, 10_005)
            .Select(i => Entry(i, now.AddMinutes(-i)))
            .ToList();

        var result = HistoryRetention.Apply(history, now);

        Assert.Equal(10_000, result.Count);
        Assert.Equal("id-00000", result[0].Id);
        Assert.Equal("id-09999", result[^1].Id);
    }

    [Fact]
    public void ResultIsOrderedDescending()
    {
        var now = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var history = new[]
        {
            Entry(1, now.AddMinutes(-30)),
            Entry(2, now.AddMinutes(-10)),
            Entry(3, now.AddMinutes(-20)),
        };

        var result = HistoryRetention.Apply(history, now);

        Assert.Equal(new[] { "id-00002", "id-00003", "id-00001" }, result.Select(h => h.Id));
    }
}
